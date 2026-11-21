using Mma.Cli.Core.Contracts;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.CodeGen;

public interface IDryRunDiffEngine
{
    Task<OperationResult<ExecutionPlan>> ComputePlanAsync(
        string solutionPath,
        IReadOnlyList<StagedFileChange> changes,
        CancellationToken ct = default);

    Task<OperationResult<GenerationSummary>> CommitAsync(
        ExecutionPlan plan,
        CancellationToken ct = default);
}
