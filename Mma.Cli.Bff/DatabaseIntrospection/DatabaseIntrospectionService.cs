public sealed class DatabaseIntrospectionService : IDatabaseIntrospectionService, IDisposable
{
    private const int MaxConcurrentIntrospections = 2;

    private static readonly HashSet<string> MmaSystemTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "__EFMigrationsHistory", "sysdiagrams", "AppUsers", "Attachments", "ELMAH_Error",
        "NotificationStatuses", "NotificationTypes", "Roles", "SysSettings", "Notifications",
        "RefreshTokens", "UserClaims", "UserLogins", "UserTokens", "RoleClaims", "UserRoles",
    };

    private static readonly HashSet<string> MmaAuditColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "CreatedBy", "CreatedDate", "ModifiedBy", "ModifiedDate", "IsDeleted", "DeletedBy", "DeletedDate",
    };

    private readonly IReadOnlyDictionary<string, IDatabaseIntrospectionProvider> _providers;
    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrentIntrospections, MaxConcurrentIntrospections);

    public DatabaseIntrospectionService(IEnumerable<IDatabaseIntrospectionProvider> providers)
    {
        _providers = providers.ToDictionary(provider => provider.Engine, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IntrospectionResult> IntrospectAsync(
        IntrospectionRequest request,
        ValidatedDatabaseTarget target,
        CancellationToken cancellationToken)
    {
        if (!_providers.TryGetValue(request.Engine, out var provider))
        {
            throw new DatabaseIntrospectionException("No provider is registered.", 502, "database_unreachable");
        }

        if (!_concurrencyLimiter.Wait(0))
        {
            throw new DatabaseIntrospectionException("Introspection concurrency limit reached.", 429, "introspection_busy");
        }

        try
        {
            var result = await provider.IntrospectAsync(request, target, cancellationToken);
            return request.ExcludeMmaSystemTables == true ? FilterMmaSystemObjects(result) : result;
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    internal static IntrospectionResult FilterMmaSystemObjects(IntrospectionResult result)
    {
        var keptTables = new List<IntrospectionTable>();
        var keptTableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keptColumnIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var table in result.Tables)
        {
            if (MmaSystemTables.Contains(table.Name))
            {
                continue;
            }

            var keptColumns = table.Columns
                .Where(c => !MmaAuditColumns.Contains(c.Name))
                .ToList();

            keptTableIds.Add(table.Id);
            foreach (var col in keptColumns)
            {
                keptColumnIds.Add(col.Id);
            }

            keptTables.Add(table with { Columns = keptColumns });
        }

        var keptRelations = result.Relations
            .Where(r =>
                keptTableIds.Contains(r.SourceTableId) &&
                keptTableIds.Contains(r.TargetTableId) &&
                keptColumnIds.Contains(r.SourceColumnId) &&
                keptColumnIds.Contains(r.TargetColumnId))
            .ToList();

        return result with { Tables = keptTables, Relations = keptRelations };
    }

    public void Dispose() => _concurrencyLimiter.Dispose();
}
