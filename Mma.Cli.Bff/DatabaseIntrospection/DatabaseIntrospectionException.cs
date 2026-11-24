public sealed class DatabaseIntrospectionException : Exception
{
    private static readonly IReadOnlyDictionary<string, (int Status, string Message)> SafeErrors =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            ["invalid_request"] = (400, "The introspection request is invalid."),
            ["unsupported_engine"] = (400, "The requested database engine is not supported."),
            ["invalid_host"] = (400, "The database host is invalid."),
            ["invalid_port"] = (400, "The database port is invalid."),
            ["origin_not_allowed"] = (403, "The request origin is not allowed."),
            ["target_not_allowed"] = (403, "The database target is not allowed."),
            ["authentication_failed"] = (422, "The database rejected the supplied credentials."),
            ["database_not_found"] = (422, "The requested database was not found."),
            ["schema_not_found"] = (422, "The requested schema was not found."),
            ["database_unreachable"] = (502, "Could not connect to the database. Check the connection details and try again."),
            ["metadata_query_failed"] = (502, "Could not read database metadata."),
            ["connection_timeout"] = (504, "The database connection timed out."),
            ["query_timeout"] = (504, "The database metadata query timed out."),
            ["introspection_busy"] = (429, "The database introspection service is busy. Try again shortly."),
            ["internal_error"] = (500, "Database introspection failed."),
        };

    public DatabaseIntrospectionException(string providerMessage, int statusCode, string code)
        : base(GetSafeMessage(statusCode, code))
    {
        var isKnownError = SafeErrors.TryGetValue(code, out var safe) && safe.Status == statusCode;
        StatusCode = isKnownError ? statusCode : 500;
        Code = isKnownError ? code : "internal_error";
    }

    private static string GetSafeMessage(int statusCode, string code) =>
        SafeErrors.TryGetValue(code, out var safe) && safe.Status == statusCode
            ? safe.Message
            : SafeErrors["internal_error"].Message;

    public int StatusCode { get; }
    public string Code { get; }
    public string SafeMessage => Message;
}
