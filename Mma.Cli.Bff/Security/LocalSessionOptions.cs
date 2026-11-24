using System.Security.Cryptography;
using System.Text;

namespace Mma.Cli.Bff.Security;

public sealed class LocalSessionOptions
{
    public const string CookieName = "mma_ui_session";

    public string? Token { get; init; }
    public string Mode { get; init; } = "standalone";
    public string? InitialPath { get; init; }
    public string Version { get; init; } = "5.0.0";

    public bool RequiresSessionAuth => !string.IsNullOrWhiteSpace(Token);

    public string ComputeSessionCookieValue()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            return string.Empty;
        }

        var keyBytes = Encoding.UTF8.GetBytes(Token);
        var payload = Encoding.UTF8.GetBytes("mma-cli-ui-session-v1");
        var mac = HMACSHA256.HashData(keyBytes, payload);
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    public bool IsValidToken(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(Token);
        var actualBytes = Encoding.UTF8.GetBytes(candidate.Trim());
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public bool IsValidSessionCookie(string? cookieValue)
    {
        if (!RequiresSessionAuth)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(cookieValue))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(ComputeSessionCookieValue());
        var actualBytes = Encoding.UTF8.GetBytes(cookieValue.Trim());
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
