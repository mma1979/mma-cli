using Mma.Cli.Core.Events;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Core.Contracts;

public interface ISolutionService
{
    Task<OperationResult<GenerationSummary>> CreateAsync(
        CreateSolutionRequest request,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default);
}

public interface IEntityService
{
    Task<OperationResult<GenerationSummary>> GenerateAsync(
        AddEntityRequest request,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default);
}

public interface IPropertyService
{
    Task<OperationResult<GenerationSummary>> ApplyAsync(
        AddPropertyRequest request,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default);
}

public interface IRelationService
{
    Task<OperationResult<GenerationSummary>> ApplyAsync(
        AddRelationRequest request,
        IProgress<GenerationEvent>? progress = null,
        CancellationToken ct = default);
}
