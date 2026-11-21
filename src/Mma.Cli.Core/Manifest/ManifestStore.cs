using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Services;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Manifest;

public sealed class ManifestStore : IManifestStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonDocumentOptions DocumentReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private readonly ISolutionLockManager _lockManager;
    private readonly ISolutionLocator _solutionLocator;
    private readonly ILogger<ManifestStore>? _logger;

    public ManifestStore(
        ISolutionLockManager? lockManager = null,
        ISolutionLocator? solutionLocator = null,
        ILogger<ManifestStore>? logger = null)
    {
        _lockManager = lockManager ?? new SolutionLockManager();
        _solutionLocator = solutionLocator ?? new SolutionLocator();
        _logger = logger;
    }

    public async Task<OperationResult<ManifestDocument>> LoadAsync(string solutionPath, CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.CANCELLED, "Manifest load was cancelled.");
        }

        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.VALIDATION_ERROR, "Solution path cannot be empty.");
        }

        string fullSolutionPath;
        try
        {
            fullSolutionPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(solutionPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.MANIFEST_INVALID, $"Invalid solution path '{solutionPath}'.", ex.Message);
        }

        var manifestPath = GetManifestPath(fullSolutionPath);
        if (!File.Exists(manifestPath))
        {
            return OperationResult<ManifestDocument>.Failure(
                ErrorCodes.MANIFEST_INVALID,
                $"Project manifest not found at '{manifestPath}'.");
        }

        byte[] fileBytes;
        try
        {
            fileBytes = await ServiceFileHelper.ReadFileBytesSharedAsync(manifestPath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.CANCELLED, "Manifest load was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to read project manifest '{ManifestPath}'.", manifestPath);
            return OperationResult<ManifestDocument>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to read project manifest '{manifestPath}': {ex.Message}",
                ex.ToString());
        }

        var contentHash = ComputeHash(fileBytes);
        var jsonMemory = ServiceFileHelper.StripUtf8Bom(fileBytes);

        try
        {
            using var doc = JsonDocument.Parse(jsonMemory, DocumentReadOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return OperationResult<ManifestDocument>.Failure(
                    ErrorCodes.MANIFEST_INVALID,
                    $"Project manifest '{manifestPath}' must have a JSON object root.");
            }

            if (TryGetPropertyIgnoreCase(root, "schemaVersion", out var schemaElem))
            {
                if (schemaElem.ValueKind != JsonValueKind.Number || !schemaElem.TryGetInt32(out var schemaVersion))
                {
                    return OperationResult<ManifestDocument>.Failure(
                        ErrorCodes.MANIFEST_INVALID,
                        $"Invalid schemaVersion value in '{manifestPath}'.");
                }

                if (schemaVersion == 2)
                {
                    var v2Manifest = JsonSerializer.Deserialize<ProjectManifest>(jsonMemory.Span, ReadOptions);
                    if (v2Manifest is null)
                    {
                        return OperationResult<ManifestDocument>.Failure(
                            ErrorCodes.MANIFEST_INVALID,
                            $"Could not deserialize v2 manifest '{manifestPath}'.");
                    }

                    return OperationResult<ManifestDocument>.Success(new ManifestDocument(
                        NormalizeManifest(v2Manifest),
                        contentHash,
                        UpgradedFromV1: false));
                }

                if (schemaVersion < 1 || schemaVersion > 2)
                {
                    return OperationResult<ManifestDocument>.Failure(
                        ErrorCodes.MANIFEST_INVALID,
                        $"Unsupported manifest schemaVersion '{schemaVersion}' in '{manifestPath}'.");
                }
            }

            if (!HasAnyLegacyManifestKey(root))
            {
                return OperationResult<ManifestDocument>.Failure(
                    ErrorCodes.MANIFEST_INVALID,
                    $"Manifest '{manifestPath}' does not contain schemaVersion 2 or legacy v1 manifest fields.");
            }

            var upgraded = UpgradeFromV1(root, fullSolutionPath, manifestPath);
            return OperationResult<ManifestDocument>.Success(new ManifestDocument(
                NormalizeManifest(upgraded),
                contentHash,
                UpgradedFromV1: true));
        }
        catch (JsonException ex)
        {
            return OperationResult<ManifestDocument>.Failure(
                ErrorCodes.MANIFEST_INVALID,
                $"Invalid JSON in project manifest '{manifestPath}': {ex.Message}",
                ex.ToString());
        }
    }

    public async Task<OperationResult<ManifestDocument>> SaveAtomicAsync(
        string solutionPath,
        ProjectManifest manifest,
        string? expectedHash = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.CANCELLED, "Manifest save was cancelled.");
        }

        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.VALIDATION_ERROR, "Solution path cannot be empty.");
        }

        try
        {
            await using var _ = await _lockManager.AcquireAsync(solutionPath, ct: ct).ConfigureAwait(false);
            return await SaveWithHeldLockAsync(solutionPath, manifest, expectedHash, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.CANCELLED, "Manifest save was cancelled.");
        }
        catch (TimeoutException ex)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.IO_ERROR, ex.Message, ex.ToString());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.VALIDATION_ERROR, $"Invalid solution path '{solutionPath}'.", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to save manifest for '{SolutionPath}'.", solutionPath);
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.IO_ERROR, ex.Message, ex.ToString());
        }
    }

    public async Task<OperationResult<ManifestDocument>> UpdateAtomicAsync(
        string solutionPath,
        Func<ProjectManifest, OperationResult<ProjectManifest>> transform,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(transform);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.CANCELLED, "Manifest update was cancelled.");
        }

        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.VALIDATION_ERROR, "Solution path cannot be empty.");
        }

        try
        {
            await using var _ = await _lockManager.AcquireAsync(solutionPath, ct: ct).ConfigureAwait(false);

            var loaded = await LoadAsync(solutionPath, ct).ConfigureAwait(false);
            if (!loaded.IsSuccess || loaded.Value is null)
            {
                return loaded;
            }

            var transformed = transform(loaded.Value.Manifest);
            if (!transformed.IsSuccess || transformed.Value is null)
            {
                return OperationResult<ManifestDocument>.Failure(
                    transformed.Error ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Manifest transform failed."));
            }

            var nextManifest = transformed.Value.UpdatedAtUtc == loaded.Value.Manifest.UpdatedAtUtc
                ? transformed.Value with { UpdatedAtUtc = DateTimeOffset.UtcNow }
                : transformed.Value;

            return await SaveWithHeldLockAsync(solutionPath, nextManifest, loaded.Value.ContentHash, ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.CANCELLED, "Manifest update was cancelled.");
        }
        catch (TimeoutException ex)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.IO_ERROR, ex.Message, ex.ToString());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.VALIDATION_ERROR, $"Invalid solution path '{solutionPath}'.", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to update manifest for '{SolutionPath}'.", solutionPath);
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.IO_ERROR, ex.Message, ex.ToString());
        }
    }

    public async Task<OperationResult<ManifestDocument>> SaveWithHeldLockAsync(
        string solutionPath,
        ProjectManifest manifest,
        string? expectedHash = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.CANCELLED, "Manifest save was cancelled.");
        }

        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.VALIDATION_ERROR, "Solution path cannot be empty.");
        }

        string fullSolutionPath;
        try
        {
            fullSolutionPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(solutionPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.VALIDATION_ERROR, $"Invalid solution path '{solutionPath}'.", ex.Message);
        }

        var mmaDir = Path.Combine(fullSolutionPath, ".mma");
        var manifestPath = Path.Combine(mmaDir, "project.mma");
        var tmpPath = Path.Combine(mmaDir, $"project.mma.{Guid.NewGuid():N}.tmp");

        try
        {
            var dirInfo = Directory.CreateDirectory(mmaDir);
            if (OperatingSystem.IsWindows())
            {
                dirInfo.Attributes = FileAttributes.Directory | FileAttributes.Hidden;
            }

            if (expectedHash is not null)
            {
                if (!File.Exists(manifestPath))
                {
                    return OperationResult<ManifestDocument>.Failure(
                        ErrorCodes.MANIFEST_CONFLICT,
                        $"Manifest conflict: expected hash '{expectedHash}', but '{manifestPath}' does not exist.");
                }

                var currentBytes = await ServiceFileHelper.ReadFileBytesSharedAsync(manifestPath, ct).ConfigureAwait(false);
                var currentHash = ComputeHash(currentBytes);
                if (!string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    return OperationResult<ManifestDocument>.Failure(
                        ErrorCodes.MANIFEST_CONFLICT,
                        $"Manifest conflict on '{manifestPath}': expected '{expectedHash}', found '{currentHash}'.");
                }
            }

            var normalized = NormalizeManifest(manifest) with
            {
                SchemaVersion = 2,
                UpdatedAtUtc = manifest.UpdatedAtUtc == default ? DateTimeOffset.UtcNow : manifest.UpdatedAtUtc
            };

            var json = JsonSerializer.Serialize(normalized, WriteOptions);
            var writtenBytes = Utf8NoBom.GetBytes(json);

            await using (var fs = new FileStream(
                tmpPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true))
            {
                await fs.WriteAsync(writtenBytes, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }

            File.Move(tmpPath, manifestPath, overwrite: true);

            var newHash = ComputeHash(writtenBytes);
            return OperationResult<ManifestDocument>.Success(new ManifestDocument(
                normalized,
                newHash,
                UpgradedFromV1: false));
        }
        catch (OperationCanceledException)
        {
            return OperationResult<ManifestDocument>.Failure(ErrorCodes.CANCELLED, "Manifest save was cancelled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Failed to atomically write manifest '{ManifestPath}'.", manifestPath);
            return OperationResult<ManifestDocument>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to write project manifest '{manifestPath}': {ex.Message}",
                ex.ToString());
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try
                {
                    File.Delete(tmpPath);
                }
                catch (IOException)
                {
                    // Best-effort temp file cleanup.
                }
            }
        }
    }

    private ProjectManifest UpgradeFromV1(JsonElement root, string fullSolutionPath, string manifestPath)
    {
        var located = _solutionLocator.Locate(fullSolutionPath);
        var locatedInfo = located.IsSuccess ? located.Value : null;

        string? projectName = null;
        if (TryGetPropertyIgnoreCase(root, "Project", out var projectElem) && projectElem.ValueKind == JsonValueKind.Object)
        {
            projectName = GetStringOrNull(projectElem, "Name");
        }

        var solutionName = !string.IsNullOrWhiteSpace(projectName)
            ? projectName
            : locatedInfo?.SolutionName ?? Path.GetFileName(fullSolutionPath);

        var solutionFormat = locatedInfo?.SolutionFormat ?? ".sln";

        string mapper;
        if (TryGetPropertyIgnoreCase(root, "mapper", out var mapperElem)
            && mapperElem.ValueKind == JsonValueKind.String
            && MapperKindParser.TryParse(mapperElem.GetString(), out var parsedMapper))
        {
            mapper = parsedMapper.ToFolderName();
        }
        else
        {
            mapper = (locatedInfo?.DetectedMapper ?? MapperKind.AutoMapper).ToFolderName();
        }

        var topLevelRows = ParseLegacyRows(root);
        var entityNameById = new Dictionary<int, string>();
        var entities = new List<ManifestEntity>();

        if (TryGetPropertyIgnoreCase(root, "Entities", out var entitiesElem)
            && entitiesElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var entityElem in entitiesElem.EnumerateArray())
            {
                if (entityElem.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var entityName = GetStringOrNull(entityElem, "EntityName")
                                 ?? GetStringOrNull(entityElem, "Name");
                if (string.IsNullOrWhiteSpace(entityName))
                {
                    continue;
                }

                var entityId = GetIntOrNull(entityElem, "Id");
                if (entityId.HasValue)
                {
                    entityNameById[entityId.Value] = entityName;
                }

                var rawPk = GetStringOrNull(entityElem, "PkType");
                var pkType = DataTypes.Normalize(rawPk) ?? rawPk ?? "Guid";
                var hasService = GetBoolOrDefault(entityElem, "HasService", true);
                var hasApi = GetBoolOrDefault(entityElem, "HasApi", true);
                var applied = GetBoolOrDefault(entityElem, "Applied", false);

                var props = new List<ManifestProperty>();
                var seenPropNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var nestedProp in ParseEntityNestedRows(entityElem))
                {
                    if (seenPropNames.Add(nestedProp.Name))
                    {
                        props.Add(nestedProp);
                    }
                }

                if (entityId.HasValue)
                {
                    foreach (var (rowEntityId, rowProp) in topLevelRows)
                    {
                        if (rowEntityId == entityId.Value && seenPropNames.Add(rowProp.Name))
                        {
                            props.Add(rowProp);
                        }
                    }
                }

                entities.Add(new ManifestEntity(entityName, pkType, hasService, hasApi, applied, props));
            }
        }

        var entityByName = new Dictionary<string, ManifestEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in entities)
        {
            entityByName.TryAdd(entity.Name, entity);
        }

        var relations = new List<ManifestRelation>();
        if (TryGetPropertyIgnoreCase(root, "Relations", out var relationsElem)
            && relationsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var relElem in relationsElem.EnumerateArray())
            {
                if (relElem.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var parentEntity = ResolveEntityRef(
                    relElem,
                    entityNameById,
                    idPropertyName: "ParentId",
                    ["ParentEntity", "ParentEntityName", "ParentName"]);

                var childEntity = ResolveEntityRef(
                    relElem,
                    entityNameById,
                    idPropertyName: "ChildId",
                    ["ChildEntity", "ChiledEntity", "ChildEntityName", "ChiledEntityName", "ChildName", "ChiledName"]);

                var foreignKey = GetStringOrNull(relElem, "ForeignKey");
                if (string.IsNullOrWhiteSpace(foreignKey))
                {
                    foreignKey = string.IsNullOrWhiteSpace(parentEntity)
                        ? string.Empty
                        : $"{parentEntity}Id";
                }

                var rawFkType = GetStringOrNull(relElem, "ForeignKeyType");
                var foreignKeyType = !string.IsNullOrWhiteSpace(rawFkType)
                    ? DataTypes.Normalize(rawFkType) ?? rawFkType
                    : entityByName.TryGetValue(parentEntity, out var parentModel)
                        ? parentModel.PkType
                        : "Guid";

                var relName = GetStringOrNull(relElem, "Name");
                if (string.IsNullOrWhiteSpace(relName))
                {
                    relName = $"{parentEntity}_{childEntity}_{foreignKey}";
                }

                var relationType = GetStringOrNull(relElem, "RelationType") ?? "1:N";
                var deleteBehavior = GetStringOrNull(relElem, "DeleteBehavior") ?? "Cascade";
                var applied = GetBoolOrDefault(relElem, "Applied", false);

                relations.Add(new ManifestRelation(
                    relName,
                    parentEntity,
                    childEntity,
                    foreignKey,
                    foreignKeyType,
                    relationType,
                    deleteBehavior,
                    applied));
            }
        }

        DateTimeOffset updatedAtUtc;
        if (TryGetPropertyIgnoreCase(root, "updatedAtUtc", out var updatedElem)
            && updatedElem.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updatedElem.GetString(), out var parsedDate))
        {
            updatedAtUtc = parsedDate;
        }
        else
        {
            updatedAtUtc = new DateTimeOffset(File.GetLastWriteTimeUtc(manifestPath), TimeSpan.Zero);
        }

        return new ProjectManifest
        {
            SchemaVersion = 2,
            SolutionName = solutionName,
            Mapper = mapper,
            SolutionFormat = solutionFormat,
            Entities = entities,
            Relations = relations,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    private static List<(int? EntityId, ManifestProperty Property)> ParseLegacyRows(JsonElement root)
    {
        var results = new List<(int? EntityId, ManifestProperty Property)>();
        if (!TryGetPropertyIgnoreCase(root, "Rows", out var rowsElem) || rowsElem.ValueKind != JsonValueKind.Array)
        {
            return results;
        }

        foreach (var rowElem in rowsElem.EnumerateArray())
        {
            if (rowElem.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryParseLegacyProperty(rowElem, out var prop))
            {
                results.Add((GetIntOrNull(rowElem, "EntityId"), prop));
            }
        }

        return results;
    }

    private static IEnumerable<ManifestProperty> ParseEntityNestedRows(JsonElement entityElem)
    {
        foreach (var arrayName in new[] { "Rows", "Properties" })
        {
            if (!TryGetPropertyIgnoreCase(entityElem, arrayName, out var arrElem)
                || arrElem.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var rowElem in arrElem.EnumerateArray())
            {
                if (rowElem.ValueKind == JsonValueKind.Object && TryParseLegacyProperty(rowElem, out var prop))
                {
                    yield return prop;
                }
            }
        }
    }

    private static bool TryParseLegacyProperty(JsonElement rowElem, out ManifestProperty property)
    {
        var colName = GetStringOrNull(rowElem, "ColumnName") ?? GetStringOrNull(rowElem, "Name");
        if (string.IsNullOrWhiteSpace(colName))
        {
            property = null!;
            return false;
        }

        var rawType = GetStringOrNull(rowElem, "DataType");
        var dataType = DataTypes.Normalize(rawType) ?? rawType ?? "string";
        var nullable = GetBoolOrDefault(rowElem, "Nullable", false);
        var isForeignKey = GetBoolOrDefault(rowElem, "IsForeignKey", false);
        var applied = GetBoolOrDefault(rowElem, "Applied", false);
        var maxLength = GetIntOrNull(rowElem, "MaxLength");
        var required = GetBoolOrNull(rowElem, "Required");

        property = new ManifestProperty(colName, dataType, nullable, isForeignKey, applied, maxLength, required);
        return true;
    }

    private static string ResolveEntityRef(
        JsonElement relElem,
        IReadOnlyDictionary<int, string> entityNameById,
        string idPropertyName,
        ReadOnlySpan<string> candidateNames)
    {
        foreach (var candidate in candidateNames)
        {
            if (!TryGetPropertyIgnoreCase(relElem, candidate, out var propElem))
            {
                continue;
            }

            if (propElem.ValueKind == JsonValueKind.String)
            {
                var str = propElem.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    return str;
                }
            }
            else if (propElem.ValueKind == JsonValueKind.Object)
            {
                var nested = GetStringOrNull(propElem, "EntityName") ?? GetStringOrNull(propElem, "Name");
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        var entityId = GetIntOrNull(relElem, idPropertyName);
        if (entityId.HasValue && entityNameById.TryGetValue(entityId.Value, out var resolvedName))
        {
            return resolvedName;
        }

        return string.Empty;
    }

    private static bool HasAnyLegacyManifestKey(JsonElement root) =>
        TryGetPropertyIgnoreCase(root, "Project", out _)
        || TryGetPropertyIgnoreCase(root, "Entities", out _)
        || TryGetPropertyIgnoreCase(root, "Rows", out _)
        || TryGetPropertyIgnoreCase(root, "Relations", out _);

    private static ProjectManifest NormalizeManifest(ProjectManifest manifest) =>
        manifest with
        {
            SolutionName = manifest.SolutionName ?? string.Empty,
            Mapper = string.IsNullOrWhiteSpace(manifest.Mapper) ? "AutoMapper" : manifest.Mapper,
            SolutionFormat = string.IsNullOrWhiteSpace(manifest.SolutionFormat) ? ".sln" : manifest.SolutionFormat,
            Entities = manifest.Entities
                .Select(e => e with
                {
                    PkType = string.IsNullOrWhiteSpace(e.PkType) ? "Guid" : e.PkType,
                    Properties = [.. e.Properties]
                })
                .ToList(),
            Relations = [.. manifest.Relations]
        };

    private static string GetManifestPath(string fullSolutionPath) =>
        Path.Combine(fullSolutionPath, ".mma", "project.mma");

    private static string ComputeHash(byte[] bytes) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static bool TryGetPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetStringOrNull(JsonElement obj, string name) =>
        TryGetPropertyIgnoreCase(obj, name, out var elem) && elem.ValueKind == JsonValueKind.String
            ? elem.GetString()
            : null;

    private static int? GetIntOrNull(JsonElement obj, string name) =>
        TryGetPropertyIgnoreCase(obj, name, out var elem)
        && elem.ValueKind == JsonValueKind.Number
        && elem.TryGetInt32(out var val)
            ? val
            : null;

    private static bool GetBoolOrDefault(JsonElement obj, string name, bool defaultValue) =>
        GetBoolOrNull(obj, name) ?? defaultValue;

    private static bool? GetBoolOrNull(JsonElement obj, string name)
    {
        if (!TryGetPropertyIgnoreCase(obj, name, out var elem))
        {
            return null;
        }

        return elem.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}
