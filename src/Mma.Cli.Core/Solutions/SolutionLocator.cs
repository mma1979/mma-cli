using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Solutions;

public interface ISolutionLocator
{
    OperationResult<SolutionInfo> Locate(string solutionPath);
}

public sealed class SolutionLocator : ISolutionLocator
{
    private readonly ILogger<SolutionLocator>? _logger;

    public SolutionLocator(ILogger<SolutionLocator>? logger = null)
    {
        _logger = logger;
    }

    public OperationResult<SolutionInfo> Locate(string solutionPath)
    {
        if (string.IsNullOrWhiteSpace(solutionPath))
        {
            return OperationResult<SolutionInfo>.Failure(
                ErrorCodes.SOLUTION_NOT_FOUND,
                "Solution path cannot be empty.");
        }

        string fullSolutionPath;
        try
        {
            fullSolutionPath = Path.GetFullPath(solutionPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<SolutionInfo>.Failure(
                ErrorCodes.SOLUTION_NOT_FOUND,
                $"Invalid solution path: '{solutionPath}'.",
                ex.Message);
        }

        if (!Directory.Exists(fullSolutionPath))
        {
            return OperationResult<SolutionInfo>.Failure(
                ErrorCodes.SOLUTION_NOT_FOUND,
                $"Solution directory does not exist: '{fullSolutionPath}'.");
        }

        var slnFile = Directory.EnumerateFiles(fullSolutionPath, "*.slnx", SearchOption.TopDirectoryOnly)
                          .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                          .FirstOrDefault()
                      ?? Directory.EnumerateFiles(fullSolutionPath, "*.sln", SearchOption.TopDirectoryOnly)
                          .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                          .FirstOrDefault();

        if (slnFile is null)
        {
            return OperationResult<SolutionInfo>.Failure(
                ErrorCodes.SOLUTION_NOT_FOUND,
                $"No .slnx or .sln file found in '{fullSolutionPath}'.");
        }

        var solutionName = Path.GetFileNameWithoutExtension(slnFile);
        var solutionFormat = Path.GetExtension(slnFile).ToLowerInvariant();
        var projectsPath = Path.Combine(fullSolutionPath, solutionName);
        var detectedMapper = DetectMapper(fullSolutionPath);

        return OperationResult<SolutionInfo>.Success(new SolutionInfo(
            solutionName,
            fullSolutionPath,
            projectsPath,
            slnFile,
            solutionFormat,
            detectedMapper));
    }

    private MapperKind DetectMapper(string fullSolutionPath)
    {
        var manifestPath = Path.Combine(fullSolutionPath, ".mma", "project.mma");
        if (File.Exists(manifestPath))
        {
            try
            {
                using var stream = File.OpenRead(manifestPath);
                using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        if (string.Equals(prop.Name, "mapper", StringComparison.OrdinalIgnoreCase) &&
                            prop.Value.ValueKind == JsonValueKind.String &&
                            MapperKindParser.TryParse(prop.Value.GetString(), out var parsedMapper))
                        {
                            return parsedMapper;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Failed to inspect manifest '{ManifestPath}' for mapper detection.", manifestPath);
            }
        }

        try
        {
            return Directory.EnumerateFiles(fullSolutionPath, "MappingProfile.cs", SearchOption.AllDirectories).Any()
                ? MapperKind.AutoMapper
                : MapperKind.Mapster;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Failed to scan '{SolutionPath}' for MappingProfile.cs.", fullSolutionPath);
            return MapperKind.Mapster;
        }
    }
}
