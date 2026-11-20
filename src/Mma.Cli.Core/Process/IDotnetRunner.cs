using Mma.Cli.Core.Events;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Process;

public sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);

public interface IDotnetRunner
{
    Task<OperationResult<ProcessExecutionResult>> BuildAsync(
        string workingDirectory,
        string configuration = "Debug",
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default);

    Task<OperationResult<ProcessExecutionResult>> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default);
}
