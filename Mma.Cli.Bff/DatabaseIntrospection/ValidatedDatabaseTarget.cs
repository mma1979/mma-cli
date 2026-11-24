using System.Net;

public sealed record ValidatedDatabaseTarget(string OriginalHost, IPAddress Address, int Port);
