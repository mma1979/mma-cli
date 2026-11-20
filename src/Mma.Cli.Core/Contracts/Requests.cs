using Mma.Cli.Core.Domain;

namespace Mma.Cli.Core.Contracts;

public sealed record CreateSolutionRequest(
    string ParentDirectory,
    string SolutionName,
    MapperKind Mapper = MapperKind.AutoMapper,
    bool DryRun = false);

public sealed record AddEntityRequest(
    string SolutionPath,
    string EntityName,
    string PkType = "Guid",
    MapperKind? Mapper = null,
    bool GenerateApi = true,
    bool DryRun = false);

public sealed record AddPropertyRequest(
    string SolutionPath,
    string EntityName,
    string PropertyName,
    string DataType = "Guid",
    bool Nullable = false,
    bool Remove = false,
    bool DryRun = false);

public sealed record AddRelationRequest(
    string SolutionPath,
    string ParentEntity,
    string ChildEntity,
    string ForeignKey,
    string ForeignKeyType = "Guid",
    bool Remove = false,
    bool DryRun = false);

public sealed record GenerationSummary(
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> DeletedFiles,
    bool DryRun);
