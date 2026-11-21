using Microsoft.Extensions.Primitives;
using Mma.Cli.Bff.Services;

namespace Mma.Cli.Bff.Security;

public sealed class LocalhostSecurityMiddleware
{
    private static readonly HashSet<string> AllowedLoopbackHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "127.0.0.1",
        "localhost",
        "::1",
        "[::1]"
    };

    private static readonly HashSet<string> StateChangingMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    private readonly RequestDelegate _next;
    private readonly BffSecurityOptions _options;
    private readonly BootstrapSessionManager _sessionManager;

    public LocalhostSecurityMiddleware(
        RequestDelegate next,
        BffSecurityOptions options,
        BootstrapSessionManager sessionManager)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(sessionManager);

        _next = next;
        _options = options;
        _sessionManager = sessionManager;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsValidLoopbackHost(context.Request.Host))
        {
            await BffProblemDetails.WriteAsync(
                context,
                BffErrorCodes.INVALID_HOST,
                $"Host '{context.Request.Host.Value}' is not an allowed loopback address.")
                .ConfigureAwait(false);
            return;
        }

        if (!IsAllowedOrigin(context.Request.Headers))
        {
            await BffProblemDetails.WriteAsync(
                context,
                BffErrorCodes.ORIGIN_NOT_ALLOWED,
                "Request Origin header is not an allowed loopback origin.")
                .ConfigureAwait(false);
            return;
        }

        var path = context.Request.Path;
        var method = context.Request.Method;

        if (IsProtectedApiRoute(path, method))
        {
            var cookieValue = context.Request.Cookies.TryGetValue(_options.CookieName, out var rawCookie)
                ? rawCookie
                : null;

            if (!_sessionManager.TryGetSession(cookieValue, out var session) || session is null)
            {
                await BffProblemDetails.WriteAsync(
                    context,
                    BffErrorCodes.UNAUTHORIZED,
                    "A valid BFF session cookie is required.")
                    .ConfigureAwait(false);
                return;
            }

            if (StateChangingMethods.Contains(method))
            {
                var csrfHeader = context.Request.Headers.TryGetValue(_options.CsrfHeaderName, out var rawCsrf)
                    ? rawCsrf.ToString()
                    : null;

                if (!_sessionManager.ValidateCsrf(session, csrfHeader))
                {
                    await BffProblemDetails.WriteAsync(
                        context,
                        BffErrorCodes.CSRF_INVALID,
                        $"Missing or invalid '{_options.CsrfHeaderName}' CSRF header.")
                        .ConfigureAwait(false);
                    return;
                }
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool IsValidLoopbackHost(HostString host) =>
        host.HasValue && !string.IsNullOrWhiteSpace(host.Host) && AllowedLoopbackHosts.Contains(host.Host);

    private static bool IsAllowedOrigin(IHeaderDictionary headers)
    {
        if (!headers.TryGetValue("Origin", out var originValues) || StringValues.IsNullOrEmpty(originValues))
        {
            return true;
        }

        if (originValues.Count != 1)
        {
            return false;
        }

        var origin = originValues[0];
        if (string.IsNullOrWhiteSpace(origin) || string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        if (!string.Equals(originUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(originUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AllowedLoopbackHosts.Contains(originUri.Host) || AllowedLoopbackHosts.Contains(originUri.DnsSafeHost);
    }

    private static bool IsProtectedApiRoute(PathString path, string method)
    {
        if (path.StartsWithSegments("/openapi", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HttpMethods.IsGet(method) && path.Equals("/api/v1/health", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HttpMethods.IsPost(method) && path.Equals("/api/v1/auth/bootstrap", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HttpMethods.IsGet(method) && path.Equals("/api/v1/auth/session", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
