public sealed class DatabaseIntrospectionService : IDatabaseIntrospectionService, IDisposable
{
    private const int MaxConcurrentIntrospections = 2;

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
            return await provider.IntrospectAsync(request, target, cancellationToken);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }

    public void Dispose() => _concurrencyLimiter.Dispose();
}
