using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Export;

public interface ISolutionExporter
{
    Task<OperationResult> ExportAsync(string solutionPath, CancellationToken ct = default);
}
