using Mma.Cli.Core.Results;

namespace Mma.Cli.Database.Introspection;

public interface IDatabaseIntrospector
{
    DatabaseProviderKind Provider { get; }

    OperationResult<string> BuildConnectionString(DatabaseConnectionParameters parameters);

    Task<OperationResult> TestConnectionAsync(
        DatabaseIntrospectRequest request,
        CancellationToken ct = default);

    Task<OperationResult<DatabaseSchemaSnapshot>> IntrospectAsync(
        DatabaseIntrospectRequest request,
        CancellationToken ct = default);
}
