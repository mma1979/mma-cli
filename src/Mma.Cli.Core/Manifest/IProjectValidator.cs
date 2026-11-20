using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Manifest;

public sealed record ValidationIssue(
    string Severity,
    string Code,
    string Message,
    string? Target = null);

public sealed record ProjectValidationReport(
    bool IsValid,
    string SolutionName,
    IReadOnlyList<ValidationIssue> Issues);

public interface IProjectValidator
{
    Task<OperationResult<ProjectValidationReport>> ValidateAsync(
        string solutionPath,
        CancellationToken ct = default);
}
