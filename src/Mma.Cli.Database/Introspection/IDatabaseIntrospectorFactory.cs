namespace Mma.Cli.Database.Introspection;

public interface IDatabaseIntrospectorFactory
{
    IDatabaseIntrospector GetIntrospector(DatabaseProviderKind provider);
}
