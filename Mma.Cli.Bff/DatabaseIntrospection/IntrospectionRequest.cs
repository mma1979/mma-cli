public sealed record IntrospectionRequest
{
    public required string Engine { get; init; }
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Database { get; init; }
    public required string Username { get; init; }
    public string? Password { get; init; }
    public string? SchemaFilter { get; init; }
    public bool? Ssl { get; init; }
}
