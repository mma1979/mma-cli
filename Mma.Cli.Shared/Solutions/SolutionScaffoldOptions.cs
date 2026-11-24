namespace Mma.Cli.Shared.Solutions;

public sealed record SolutionScaffoldOptions(
    string SolutionName,
    string ParentDirectory,
    string Mapper,
    string? TemplateSource = null,
    string? TemplateVersion = null,
    bool ForceInstall = false);

public sealed record TemplateCommandResult(
    bool Succeeded,
    string StdOut,
    string StdErr,
    int ExitCode);
