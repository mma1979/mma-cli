using Mma.Cli.Core.Domain;
using Mma.Cli.Core.Results;
using Mma.Cli.Core.Solutions;

namespace Mma.Cli.Core.Manifest;

public sealed class ProjectValidator : IProjectValidator
{
    private const string SeverityError = "Error";
    private const string SeverityWarning = "Warning";

    private readonly ISolutionLocator _solutionLocator;
    private readonly IManifestStore _manifestStore;

    public ProjectValidator(
        ISolutionLocator solutionLocator,
        IManifestStore manifestStore)
    {
        ArgumentNullException.ThrowIfNull(solutionLocator);
        ArgumentNullException.ThrowIfNull(manifestStore);

        _solutionLocator = solutionLocator;
        _manifestStore = manifestStore;
    }

    public async Task<OperationResult<ProjectValidationReport>> ValidateAsync(
        string solutionPath,
        CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested)
        {
            return OperationResult<ProjectValidationReport>.Failure(
                ErrorCodes.CANCELLED,
                "Project validation was cancelled.");
        }

        var located = _solutionLocator.Locate(solutionPath);
        if (!located.IsSuccess || located.Value is null)
        {
            return OperationResult<ProjectValidationReport>.Failure(
                located.Error ?? new GenerationError(ErrorCodes.SOLUTION_NOT_FOUND, "Solution could not be located."));
        }

        var info = located.Value;
        var loaded = await _manifestStore.LoadAsync(info.SolutionPath, ct).ConfigureAwait(false);
        if (!loaded.IsSuccess || loaded.Value is null)
        {
            return OperationResult<ProjectValidationReport>.Failure(
                loaded.Error ?? new GenerationError(ErrorCodes.MANIFEST_INVALID, "Project manifest could not be loaded."));
        }

        var manifest = loaded.Value.Manifest;
        var issues = new List<ValidationIssue>();

        ValidateEntities(info, manifest.Entities, issues);
        ValidateRelations(manifest.Entities, manifest.Relations, issues);

        var isValid = !issues.Any(i => string.Equals(i.Severity, SeverityError, StringComparison.OrdinalIgnoreCase));
        var reportSolutionName = string.IsNullOrWhiteSpace(manifest.SolutionName)
            ? info.SolutionName
            : manifest.SolutionName;

        return OperationResult<ProjectValidationReport>.Success(
            new ProjectValidationReport(isValid, reportSolutionName, issues));
    }

    private static void ValidateEntities(
        SolutionInfo info,
        IReadOnlyList<ManifestEntity> entities,
        List<ValidationIssue> issues)
    {
        var seenEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in entities)
        {
            if (!seenEntities.Add(entity.Name))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "DUPLICATE_ENTITY",
                    $"Duplicate entity '{entity.Name}' in manifest.",
                    entity.Name));
            }

            if (!NamingHelper.IsValidIdentifier(entity.Name))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "INVALID_ENTITY_NAME",
                    $"Entity name '{entity.Name}' is not a valid C# identifier.",
                    entity.Name));
            }

            if (!DataTypes.IsSupported(entity.PkType))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "INVALID_PK_TYPE",
                    $"Entity '{entity.Name}' has unsupported primary key type '{entity.PkType}'.",
                    entity.Name));
            }

            ValidateProperties(entity, issues);

            if (entity.Applied)
            {
                if (NamingHelper.IsValidIdentifier(entity.Name))
                {
                    var expectedEntityFile = Path.Combine(
                        info.ProjectsPath,
                        $"{info.SolutionName}.Core",
                        "Database",
                        "Tables",
                        $"{entity.Name}.cs");

                    if (!File.Exists(expectedEntityFile))
                    {
                        issues.Add(new ValidationIssue(
                            SeverityError,
                            "DRIFT_MISSING_ENTITY_FILE",
                            $"Applied entity '{entity.Name}' is missing expected source file '{expectedEntityFile}'.",
                            entity.Name));
                    }
                }
            }
            else
            {
                issues.Add(new ValidationIssue(
                    SeverityWarning,
                    "UNAPPLIED_ENTITY",
                    $"Entity '{entity.Name}' is tracked in manifest as unapplied.",
                    entity.Name));
            }
        }
    }

    private static void ValidateProperties(ManifestEntity entity, List<ValidationIssue> issues)
    {
        var seenProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prop in entity.Properties)
        {
            var target = $"{entity.Name}.{prop.Name}";

            if (!seenProps.Add(prop.Name))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "DUPLICATE_PROPERTY",
                    $"Duplicate property '{prop.Name}' on entity '{entity.Name}'.",
                    target));
            }

            if (!NamingHelper.IsValidIdentifier(prop.Name))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "INVALID_PROPERTY_NAME",
                    $"Property '{prop.Name}' on entity '{entity.Name}' is not a valid C# identifier.",
                    target));
            }

            if (!DataTypes.IsSupported(prop.DataType))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "INVALID_PROPERTY_TYPE",
                    $"Property '{target}' has unsupported data type '{prop.DataType}'.",
                    target));
            }
        }
    }

    private static void ValidateRelations(
        IReadOnlyList<ManifestEntity> entities,
        IReadOnlyList<ManifestRelation> relations,
        List<ValidationIssue> issues)
    {
        var knownEntities = new HashSet<string>(
            entities.Select(e => e.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var relation in relations)
        {
            if (string.IsNullOrWhiteSpace(relation.ParentEntity) || !knownEntities.Contains(relation.ParentEntity))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "MISSING_RELATION_PARENT",
                    $"Relation '{relation.Name}' references missing parent entity '{relation.ParentEntity}'.",
                    relation.Name));
            }

            if (string.IsNullOrWhiteSpace(relation.ChildEntity) || !knownEntities.Contains(relation.ChildEntity))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "MISSING_RELATION_CHILD",
                    $"Relation '{relation.Name}' references missing child entity '{relation.ChildEntity}'.",
                    relation.Name));
            }

            if (!NamingHelper.IsValidIdentifier(relation.ForeignKey))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "INVALID_FOREIGN_KEY",
                    $"Relation '{relation.Name}' has invalid foreign key identifier '{relation.ForeignKey}'.",
                    relation.Name));
            }

            if (!DataTypes.IsSupported(relation.ForeignKeyType))
            {
                issues.Add(new ValidationIssue(
                    SeverityError,
                    "INVALID_FOREIGN_KEY_TYPE",
                    $"Relation '{relation.Name}' has unsupported foreign key type '{relation.ForeignKeyType}'.",
                    relation.Name));
            }
        }
    }
}
