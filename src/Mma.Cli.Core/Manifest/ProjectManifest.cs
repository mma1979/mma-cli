namespace Mma.Cli.Core.Manifest;

public sealed record ProjectManifest
{
    private readonly List<ManifestEntity> _entities = [];
    private readonly List<ManifestRelation> _relations = [];

    public int SchemaVersion { get; init; } = 2;
    public string SolutionName { get; init; } = string.Empty;
    public string Mapper { get; init; } = "AutoMapper";
    public string SolutionFormat { get; init; } = ".sln";

    public List<ManifestEntity> Entities
    {
        get => _entities;
        init => _entities = value ?? [];
    }

    public List<ManifestRelation> Relations
    {
        get => _relations;
        init => _relations = value ?? [];
    }

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ManifestEntity(
    string Name,
    string PkType = "Guid",
    bool HasService = true,
    bool HasApi = true,
    bool Applied = false,
    List<ManifestProperty>? Properties = null)
{
    private readonly List<ManifestProperty> _properties = Properties ?? [];

    public List<ManifestProperty> Properties
    {
        get => _properties;
        init => _properties = value ?? [];
    }
}

public sealed record ManifestProperty(
    string Name,
    string DataType,
    bool Nullable = false,
    bool IsForeignKey = false,
    bool Applied = false,
    int? MaxLength = null,
    bool? Required = null);

public sealed record ManifestRelation(
    string Name,
    string ParentEntity,
    string ChildEntity,
    string ForeignKey,
    string ForeignKeyType = "Guid",
    string RelationType = "1:N",
    string DeleteBehavior = "Cascade",
    bool Applied = false);

public sealed record ManifestDocument(
    ProjectManifest Manifest,
    string ContentHash,
    bool UpgradedFromV1);
