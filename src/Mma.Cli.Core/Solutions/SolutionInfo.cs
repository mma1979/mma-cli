using Mma.Cli.Core.Domain;

namespace Mma.Cli.Core.Solutions;

public sealed record SolutionInfo(
    string SolutionName,
    string SolutionPath,
    string ProjectsPath,
    string SolutionFilePath,
    string SolutionFormat,
    MapperKind DetectedMapper);
