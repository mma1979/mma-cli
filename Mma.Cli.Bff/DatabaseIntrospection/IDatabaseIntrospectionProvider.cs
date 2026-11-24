public interface IDatabaseIntrospectionProvider
{
    string Engine { get; }

    Task<IntrospectionResult> IntrospectAsync(
        IntrospectionRequest request,
        ValidatedDatabaseTarget target,
        CancellationToken cancellationToken);
}
