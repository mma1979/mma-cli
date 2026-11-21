namespace Mma.Cli.Database.Introspection;

public sealed class DatabaseIntrospectorFactory : IDatabaseIntrospectorFactory
{
    private readonly Dictionary<DatabaseProviderKind, IDatabaseIntrospector> _introspectors;

    public DatabaseIntrospectorFactory(IEnumerable<IDatabaseIntrospector> introspectors)
    {
        ArgumentNullException.ThrowIfNull(introspectors);

        _introspectors = new Dictionary<DatabaseProviderKind, IDatabaseIntrospector>();
        foreach (var introspector in introspectors)
        {
            ArgumentNullException.ThrowIfNull(introspector);
            _introspectors[introspector.Provider] = introspector;
        }
    }

    public IDatabaseIntrospector GetIntrospector(DatabaseProviderKind provider)
    {
        if (_introspectors.TryGetValue(provider, out var introspector))
        {
            return introspector;
        }

        throw new NotSupportedException($"Database provider '{provider}' is not supported.");
    }
}
