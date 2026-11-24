namespace Mma.Cli.Shared.Solutions;

public sealed record SolutionTemplateDescriptor(
    string Mapper,
    string PackageId,
    string ShortName,
    string DefaultVersion);
