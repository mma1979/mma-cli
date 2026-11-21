using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Mma.Cli.Bff.Services;

namespace Mma.Cli.Bff.Security;

public sealed record SessionEntry(string SessionId, string CsrfToken, DateTimeOffset CreatedAtUtc);

public sealed class BootstrapSessionManager
{
    private readonly BffSecurityOptions _options;
    private readonly Lock _tokenLock = new();
    private readonly ConcurrentDictionary<string, SessionEntry> _sessions = new(StringComparer.Ordinal);
    private string? _pendingBootstrapToken;
    private bool _bootstrapTokenConsumed;

    public BootstrapSessionManager(BffSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _pendingBootstrapToken = string.IsNullOrWhiteSpace(options.BootstrapToken)
            ? null
            : options.BootstrapToken;
    }

    public bool TryExchangeBootstrapToken(
        string? presentedToken,
        out SessionEntry? session,
        out string errorCode)
    {
        lock (_tokenLock)
        {
            if (_bootstrapTokenConsumed)
            {
                session = null;
                errorCode = BffErrorCodes.AUTH_TOKEN_CONSUMED;
                return false;
            }

            if (string.IsNullOrWhiteSpace(_pendingBootstrapToken) || string.IsNullOrWhiteSpace(presentedToken))
            {
                session = null;
                errorCode = BffErrorCodes.AUTH_TOKEN_INVALID;
                return false;
            }

            var expectedBytes = Encoding.UTF8.GetBytes(_pendingBootstrapToken);
            var actualBytes = Encoding.UTF8.GetBytes(presentedToken);
            if (!CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes))
            {
                session = null;
                errorCode = BffErrorCodes.AUTH_TOKEN_INVALID;
                return false;
            }

            _bootstrapTokenConsumed = true;
            _pendingBootstrapToken = null;
            session = CreateSessionCore();
            errorCode = string.Empty;
            return true;
        }
    }

    public SessionEntry? CreateDevSessionIfAllowed()
    {
        if (!_options.AllowDevAutoSession || !string.IsNullOrWhiteSpace(_options.BootstrapToken))
        {
            return null;
        }

        return CreateSessionCore();
    }

    public bool TryGetSession(string? sessionId, out SessionEntry? session)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            session = null;
            return false;
        }

        return _sessions.TryGetValue(sessionId, out session);
    }

    public bool ValidateCsrf(SessionEntry session, string? headerToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(headerToken))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(session.CsrfToken);
        var actualBytes = Encoding.UTF8.GetBytes(headerToken);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private SessionEntry CreateSessionCore()
    {
        var sessionId = GenerateSecretToken();
        var csrfToken = GenerateSecretToken();
        var entry = new SessionEntry(sessionId, csrfToken, DateTimeOffset.UtcNow);
        _sessions[sessionId] = entry;
        return entry;
    }

    private static string GenerateSecretToken() =>
        Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
}
