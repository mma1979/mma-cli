public interface IDatabaseIntrospectionService
{
    Task<IntrospectionResult> IntrospectAsync(
        IntrospectionRequest request,
        ValidatedDatabaseTarget target,
        CancellationToken cancellationToken);
}
