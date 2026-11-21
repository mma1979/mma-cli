namespace Mma.Cli.Bff.Security;

public interface IMmaIdentityContext
{
    string UserId { get; }
    string DisplayName { get; }
    bool IsAuthenticated { get; }
}

public sealed class LocalSingleUserIdentity : IMmaIdentityContext
{
    public string UserId => "local-user";

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Environment.UserName) ? "Local User" : Environment.UserName;

    public bool IsAuthenticated => true;
}
