using System.Text;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Services;

internal static class ServiceFileHelper
{
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<(ProjectManifest? Manifest, string? ExpectedHash, GenerationError? Error)> LoadOrCreateManifestAsync(
        IManifestStore manifestStore,
        SolutionInfo info,
        MapperKind? overrideMapper,
        CancellationToken ct)
    {
        var manifestPath = Path.Combine(info.SolutionPath, ".mma", "project.mma");
        if (!File.Exists(manifestPath))
        {
            var minimal = new ProjectManifest
            {
                SchemaVersion = 2,
                SolutionName = info.SolutionName,
                Mapper = (overrideMapper ?? info.DetectedMapper).ToFolderName(),
                SolutionFormat = info.SolutionFormat,
                Entities = [],
                Relations = [],
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            return (minimal, null, null);
        }

        var loaded = await manifestStore.LoadAsync(info.SolutionPath, ct).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return (null, null, loaded.Error ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Failed to load manifest."));
        }

        return (loaded.Value.Manifest, loaded.Value.ContentHash, null);
    }

    public static Task<OperationResult<ManifestDocument>> SaveManifestUnderLockAsync(
        IManifestStore manifestStore,
        string solutionPath,
        ProjectManifest manifest,
        string? expectedHash,
        CancellationToken ct) =>
        manifestStore.SaveWithHeldLockAsync(solutionPath, manifest, expectedHash, ct);

    public static bool TryResolveExistingSolutionPath(
        string solutionPath,
        out string fullSolutionPath,
        out GenerationError? error)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            fullSolutionPath = string.Empty;
            error = new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Solution path cannot be empty.");
            return false;
        }

        try
        {
            fullSolutionPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(solutionPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            fullSolutionPath = string.Empty;
            error = new GenerationError(
                ErrorCodes.SOLUTION_NOT_FOUND,
                $"Invalid solution path: '{solutionPath}'.",
                ex.Message);
            return false;
        }

        if (!Directory.Exists(fullSolutionPath))
        {
            error = new GenerationError(
                ErrorCodes.SOLUTION_NOT_FOUND,
                $"Solution directory does not exist: '{fullSolutionPath}'.");
            return false;
        }

        error = null;
        return true;
    }

    public static byte[] ReadFileBytesShared(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            useAsync: false);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public static async Task<byte[]> ReadFileBytesSharedAsync(
        string path,
        CancellationToken ct,
        int maxSharingRetries = 0)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    useAsync: true);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
                return ms.ToArray();
            }
            catch (IOException ex) when (ex.GetType() == typeof(IOException) && attempt < maxSharingRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), ct).ConfigureAwait(false);
            }
        }
    }

    public static ReadOnlyMemory<byte> StripUtf8Bom(byte[] bytes)
    {
        var preamble = Encoding.UTF8.Preamble;
        return bytes.AsSpan().StartsWith(preamble)
            ? bytes.AsMemory(preamble.Length)
            : bytes.AsMemory();
    }

    public static ManifestEntity GetOrAddEntity(
        List<ManifestEntity> entities,
        string entityName,
        string pkType = "Guid",
        bool hasApi = true,
        bool applied = true)
    {
        var idx = entities.FindIndex(e => string.Equals(e.Name, entityName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            return entities[idx];
        }

        var created = new ManifestEntity(entityName, pkType, HasService: true, HasApi: hasApi, Applied: applied, Properties: []);
        entities.Add(created);
        return created;
    }

    public static void UpsertProperty(List<ManifestProperty> properties, ManifestProperty property)
    {
        var idx = properties.FindIndex(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            properties[idx] = property;
        }
        else
        {
            properties.Add(property);
        }
    }

    public static void Report(
        IProgress<GenerationEvent>? progress,
        GenerationStage stage,
        string message,
        string? filePath = null) =>
        progress?.Report(GenerationEvent.Create(stage, message, filePath));

    public static async Task<string> ReadUtf8TextAsync(string path, CancellationToken ct)
    {
        var bytes = await ReadFileBytesSharedAsync(path, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetString(StripUtf8Bom(bytes).Span);
    }
}
