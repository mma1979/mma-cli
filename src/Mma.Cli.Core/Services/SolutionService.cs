using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.SharpZipLib.Zip;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Manifest;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Services;

public sealed partial class SolutionService : ISolutionService
{
    private readonly ILogger<SolutionService>? _logger;
    private readonly ISolutionLockManager _lockManager;
    private readonly IManifestStore _manifestStore;

    public SolutionService(
        ILogger<SolutionService>? logger = null,
        ISolutionLockManager? lockManager = null,
        IManifestStore? manifestStore = null)
    {
        _logger = logger;
        _lockManager = lockManager ?? new SolutionLockManager();
        _manifestStore = manifestStore ?? new ManifestStore(_lockManager);
    }

    [GeneratedRegex(@"^.+\.(cs|json|csproj|cshtml)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NamespaceReplaceFileRegex();

    public async Task<OperationResult<GenerationSummary>> CreateAsync(
        CreateSolutionRequest request,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ct.IsCancellationRequested)
        {
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Solution creation was cancelled.");
        }

        ServiceFileHelper.Report(progress, GenerationStage.Validating, $"Validating solution '{request.SolutionName}'.");

        if (!NamingHelper.IsValidSolutionName(request.SolutionName))
        {
            ServiceFileHelper.Report(progress, GenerationStage.Failed, $"Invalid solution name '{request.SolutionName}'.");
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid solution name '{request.SolutionName}'. Use dot-separated valid C# identifiers.");
        }

        if (string.IsNullOrWhiteSpace(request.ParentDirectory))
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                "Parent directory cannot be empty.");
        }

        string parentFullPath;
        try
        {
            parentFullPath = Path.GetFullPath(request.ParentDirectory);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.VALIDATION_ERROR,
                $"Invalid parent directory '{request.ParentDirectory}'.",
                ex.Message);
        }

        var solutionPath = Path.Combine(parentFullPath, request.SolutionName);
        if (File.Exists(solutionPath))
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.IO_ERROR,
                $"Cannot create solution directory '{solutionPath}' because a file with the same name exists.");
        }

        var resourceName = request.Mapper == MapperKind.Mapster
            ? "Mma.Cli.Core.Resources.Mappster.zip"
            : "Mma.Cli.Core.Resources.AutoMapper.zip";

        var oldRoot = request.Mapper == MapperKind.Mapster
            ? "AxialSystem.Covaluse"
            : "MySolutionName";

        using var resourceStream = typeof(SolutionService).Assembly.GetManifestResourceStream(resourceName);
        if (resourceStream is null)
        {
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.TEMPLATE_ERROR,
                $"Embedded solution archive '{resourceName}' was not found.");
        }

        try
        {
            ServiceFileHelper.Report(
                progress,
                GenerationStage.Scaffolding,
                request.DryRun
                    ? $"Planning solution '{request.SolutionName}' (dry run)."
                    : $"Scaffolding solution '{request.SolutionName}'.");

            var createdFiles = new List<string>();
            var fullSolutionPrefix = solutionPath.EndsWith(Path.DirectorySeparatorChar)
                ? solutionPath
                : solutionPath + Path.DirectorySeparatorChar;
            var pathComparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var normalizedSlug = request.SolutionName.ToLowerInvariant().Replace(".", "-", StringComparison.Ordinal);

            if (!request.DryRun)
            {
                Directory.CreateDirectory(solutionPath);
            }

            IAsyncDisposable? lockHandle = null;
            try
            {
                if (!request.DryRun)
                {
                    lockHandle = await _lockManager.AcquireAsync(solutionPath, ct: ct).ConfigureAwait(false);
                }

                using var zipFile = new ZipFile(resourceStream);
                foreach (ZipEntry entry in zipFile)
                {
                    ct.ThrowIfCancellationRequested();

                    if (entry.IsDirectory)
                    {
                        continue;
                    }

                    var (relativePath, isUnderProjects, isRootSln) = TransformEntryPath(
                        entry.Name,
                        oldRoot,
                        request.SolutionName);

                    var destinationPath = Path.GetFullPath(Path.Combine(solutionPath, relativePath));
                    if (!destinationPath.StartsWith(fullSolutionPrefix, pathComparison))
                    {
                        return OperationResult<GenerationSummary>.Failure(
                            ErrorCodes.TEMPLATE_ERROR,
                            $"Unsafe archive entry path detected: '{entry.Name}'.");
                    }

                    createdFiles.Add(destinationPath);

                    if (request.DryRun)
                    {
                        continue;
                    }

                    var destDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    using var entryStream = zipFile.GetInputStream(entry);
                    if (isRootSln || (isUnderProjects && NamespaceReplaceFileRegex().IsMatch(destinationPath)))
                    {
                        using var reader = new StreamReader(entryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                        var content = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                        var rewritten = RewritePlaceholders(content, request.SolutionName, normalizedSlug);
                        await File.WriteAllTextAsync(destinationPath, rewritten, ServiceFileHelper.Utf8NoBom, ct)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        await entryStream.CopyToAsync(fileStream, ct).ConfigureAwait(false);
                    }
                }

                var manifestDir = Path.Combine(solutionPath, ".mma");
                var manifestPath = Path.Combine(manifestDir, "project.mma");
                createdFiles.Add(manifestPath);

                if (!request.DryRun)
                {
                    var solutionFormat = createdFiles.Any(f => f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
                        ? ".slnx"
                        : ".sln";

                    var manifest = new ProjectManifest
                    {
                        SchemaVersion = 2,
                        SolutionName = request.SolutionName,
                        Mapper = request.Mapper.ToFolderName(),
                        SolutionFormat = solutionFormat,
                        Entities = [],
                        Relations = [],
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    };

                    var saveResult = await ServiceFileHelper.SaveManifestUnderLockAsync(
                        _manifestStore,
                        solutionPath,
                        manifest,
                        expectedHash: null,
                        ct).ConfigureAwait(false);

                    if (!saveResult.IsSuccess)
                    {
                        return OperationResult<GenerationSummary>.Failure(
                            saveResult.Error ?? new GenerationError(ErrorCodes.IO_ERROR, "Failed to write project manifest."));
                    }
                }
            }
            finally
            {
                if (lockHandle is not null)
                {
                    await lockHandle.DisposeAsync().ConfigureAwait(false);
                }
            }

            ServiceFileHelper.Report(
                progress,
                GenerationStage.Completed,
                $"Solution '{request.SolutionName}' {(request.DryRun ? "planned" : "created")} successfully.",
                solutionPath);

            return OperationResult<GenerationSummary>.Success(new GenerationSummary(
                createdFiles,
                Array.Empty<string>(),
                Array.Empty<string>(),
                request.DryRun));
        }
        catch (OperationCanceledException)
        {
            ServiceFileHelper.Report(progress, GenerationStage.Failed, "Solution creation was cancelled.");
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.CANCELLED, "Solution creation was cancelled.");
        }
        catch (TimeoutException ex)
        {
            ServiceFileHelper.Report(progress, GenerationStage.Failed, ex.Message);
            return OperationResult<GenerationSummary>.Failure(ErrorCodes.IO_ERROR, ex.Message, ex.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ZipException)
        {
            _logger?.LogError(ex, "Failed to scaffold solution '{SolutionName}' at '{ParentDir}'.", request.SolutionName, parentFullPath);
            ServiceFileHelper.Report(progress, GenerationStage.Failed, ex.Message);
            return OperationResult<GenerationSummary>.Failure(
                ErrorCodes.IO_ERROR,
                $"Failed to scaffold solution '{request.SolutionName}': {ex.Message}",
                ex.ToString());
        }
    }

    private static (string RelativePath, bool IsUnderProjects, bool IsRootSln) TransformEntryPath(
        string entryName,
        string oldRoot,
        string solutionName)
    {
        var segments = entryName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return (string.Empty, false, false);
        }

        if (segments.Length == 1)
        {
            if (string.Equals(segments[0], $"{oldRoot}.sln", StringComparison.Ordinal))
            {
                return ($"{solutionName}.sln", false, true);
            }

            return (segments[0], false, false);
        }

        if (string.Equals(segments[0], oldRoot, StringComparison.Ordinal))
        {
            segments[0] = solutionName;
            segments[1] = segments[1].Replace(oldRoot, solutionName, StringComparison.Ordinal);

            if (segments[^1].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                segments[^1] = segments[^1].Replace(oldRoot, solutionName, StringComparison.Ordinal);
            }

            return (Path.Combine(segments), true, false);
        }

        return (Path.Combine(segments), false, false);
    }

    private static string RewritePlaceholders(string content, string solutionName, string normalizedSlug) =>
        content
            .Replace("AxialSystem.Covaluse", solutionName, StringComparison.Ordinal)
            .Replace("MySolutionName", solutionName, StringComparison.Ordinal)
            .Replace("axialsystem-covaluse", normalizedSlug, StringComparison.Ordinal)
            .Replace("axialaystem-covaluse", normalizedSlug, StringComparison.Ordinal)
            .Replace("mysolutionname", normalizedSlug, StringComparison.Ordinal);
}
