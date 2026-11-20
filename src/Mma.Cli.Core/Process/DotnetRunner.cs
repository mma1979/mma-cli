using System.Text;
using CliWrap;
using Microsoft.Extensions.Logging;
using Mma.Cli.Core.Events;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Process;

public sealed class DotnetRunner : IDotnetRunner
{
    private readonly ILogger<DotnetRunner>? _logger;

    public DotnetRunner(ILogger<DotnetRunner>? logger = null)
    {
        _logger = logger;
    }

    public Task<OperationResult<ProcessExecutionResult>> BuildAsync(
        string workingDirectory,
        string configuration = "Debug",
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default)
    {
        var effectiveConfiguration = string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration;
        return RunAsync(workingDirectory, ["build", "--configuration", effectiveConfiguration], progress, ct);
    }

    public async Task<OperationResult<ProcessExecutionResult>> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return OperationResult<ProcessExecutionResult>.Failure(
                ErrorCodes.CANCELLED,
                "dotnet execution was cancelled.");
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            return OperationResult<ProcessExecutionResult>.Failure(
                ErrorCodes.IO_ERROR,
                $"Working directory does not exist: '{workingDirectory}'.");
        }

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var outputLock = new Lock();

        progress?.Report(GenerationEvent.Create(
            GenerationStage.Starting,
            $"Running dotnet {string.Join(' ', arguments)}"));

        try
        {
            var result = await CliWrap.Cli.Wrap("dotnet")
                .WithWorkingDirectory(workingDirectory)
                .WithArguments(arguments)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.ToDelegate(line =>
                {
                    lock (outputLock)
                    {
                        stdoutBuilder.AppendLine(line);
                    }
                    progress?.Report(GenerationEvent.Create(GenerationStage.ProcessOutput, line));
                }))
                .WithStandardErrorPipe(PipeTarget.ToDelegate(line =>
                {
                    lock (outputLock)
                    {
                        stderrBuilder.AppendLine(line);
                    }
                    progress?.Report(GenerationEvent.Create(GenerationStage.ProcessOutput, line));
                }))
                .ExecuteAsync(ct)
                .ConfigureAwait(false);

            var stdout = stdoutBuilder.ToString();
            var stderr = stderrBuilder.ToString();
            var executionResult = new ProcessExecutionResult(result.ExitCode, stdout, stderr);

            if (result.ExitCode != 0)
            {
                var details = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                progress?.Report(GenerationEvent.Create(
                    GenerationStage.Failed,
                    $"dotnet exited with code {result.ExitCode}."));
                return OperationResult<ProcessExecutionResult>.Failure(
                    ErrorCodes.PROCESS_FAILED,
                    $"dotnet exited with code {result.ExitCode}.",
                    details);
            }

            progress?.Report(GenerationEvent.Create(GenerationStage.Completed, "dotnet command completed."));
            return OperationResult<ProcessExecutionResult>.Success(executionResult);
        }
        catch (OperationCanceledException)
        {
            progress?.Report(GenerationEvent.Create(GenerationStage.Failed, "dotnet execution was cancelled."));
            return OperationResult<ProcessExecutionResult>.Failure(
                ErrorCodes.CANCELLED,
                "dotnet execution was cancelled.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to run dotnet in '{WorkingDirectory}'.", workingDirectory);
            progress?.Report(GenerationEvent.Create(GenerationStage.Failed, ex.Message));
            return OperationResult<ProcessExecutionResult>.Failure(
                ErrorCodes.PROCESS_FAILED,
                $"Failed to execute dotnet: {ex.Message}",
                ex.ToString());
        }
    }
}
