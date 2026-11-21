using System.Reflection;
using Mma.Cli.Bff.Models;
using Mma.Cli.Bff.Security;
using Mma.Cli.Bff.Services;
using Mma.Cli.Core.Process;

namespace Mma.Cli.Bff.Endpoints;

public static class HealthAndAuthEndpoints
{
    public static IEndpointRouteBuilder MapHealthAndAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Health & Auth");

        group.MapGet("/health", async (
            IHostEnvironment environment,
            IDotnetRunner dotnetRunner,
            CancellationToken ct) =>
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            var workDir = Directory.Exists(Directory.GetCurrentDirectory())
                ? Directory.GetCurrentDirectory()
                : AppContext.BaseDirectory;

            var sdkTask = dotnetRunner.RunAsync(workDir, ["--version"], ct: timeoutCts.Token);
            var efTask = dotnetRunner.RunAsync(workDir, ["ef", "--version"], ct: timeoutCts.Token);
            await Task.WhenAll(sdkTask, efTask).ConfigureAwait(false);

            var sdkRes = await sdkTask.ConfigureAwait(false);
            var efRes = await efTask.ConfigureAwait(false);

            var dotnetVersion = sdkRes.IsSuccess ? FirstLine(sdkRes.Value?.StandardOutput) : null;
            var dotnetEfAvailable = efRes.IsSuccess && !string.IsNullOrWhiteSpace(FirstLine(efRes.Value?.StandardOutput));
            var version = typeof(HealthAndAuthEndpoints).Assembly.GetName().Version?.ToString() ?? "5.0.0";

            return Results.Ok(new HealthResponse(
                "healthy",
                version,
                environment.EnvironmentName,
                dotnetVersion,
                dotnetEfAvailable,
                DateTimeOffset.UtcNow));
        })
        .WithName("GetHealth")
        .Produces<HealthResponse>(StatusCodes.Status200OK);

        group.MapPost("/auth/bootstrap", (
            BootstrapAuthRequest request,
            BootstrapSessionManager sessionManager,
            BffSecurityOptions securityOptions,
            HttpContext httpContext) =>
        {
            if (!sessionManager.TryExchangeBootstrapToken(request.Token, out var session, out var errorCode)
                || session is null)
            {
                var detail = errorCode == BffErrorCodes.AUTH_TOKEN_CONSUMED
                    ? "Bootstrap token has already been consumed."
                    : "Invalid or missing bootstrap token.";
                return BffProblemDetails.ToResult(errorCode, detail);
            }

            AppendSessionCookie(httpContext, securityOptions, session);
            return Results.Ok(new BootstrapAuthResponse(true, session.CsrfToken));
        })
        .WithName("BootstrapAuth")
        .Produces<BootstrapAuthResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapGet("/auth/session", (
            BootstrapSessionManager sessionManager,
            BffSecurityOptions securityOptions,
            IMmaIdentityContext identityContext,
            HttpContext httpContext) =>
        {
            var cookieValue = httpContext.Request.Cookies.TryGetValue(securityOptions.CookieName, out var rawCookie)
                ? rawCookie
                : null;

            if (sessionManager.TryGetSession(cookieValue, out var existing) && existing is not null)
            {
                return Results.Ok(new AuthSessionResponse(
                    true,
                    existing.CsrfToken,
                    identityContext.UserId,
                    identityContext.DisplayName));
            }

            var devSession = sessionManager.CreateDevSessionIfAllowed();
            if (devSession is not null)
            {
                AppendSessionCookie(httpContext, securityOptions, devSession);
                return Results.Ok(new AuthSessionResponse(
                    true,
                    devSession.CsrfToken,
                    identityContext.UserId,
                    identityContext.DisplayName));
            }

            return Results.Ok(new AuthSessionResponse(
                false,
                null,
                identityContext.UserId,
                identityContext.DisplayName));
        })
        .WithName("GetAuthSession")
        .Produces<AuthSessionResponse>(StatusCodes.Status200OK);

        return app;
    }

    private static void AppendSessionCookie(
        HttpContext httpContext,
        BffSecurityOptions options,
        SessionEntry session)
    {
        httpContext.Response.Cookies.Append(options.CookieName, session.SessionId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true
        });
    }

    private static string? FirstLine(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                return trimmed;
            }
        }

        return null;
    }
}
