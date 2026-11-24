using System.Diagnostics;

namespace Mma.Cli.Bff.Security;

public sealed record SessionExchangeRequest(string? Token);

public static class SessionEndpoints
{
    public static void MapSessionAndHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/health", (LocalSessionOptions options) =>
            Results.Ok(new
            {
                status = "ready",
                version = options.Version,
                mode = options.Mode,
                requiresAuth = options.RequiresSessionAuth,
                initialPath = options.InitialPath,
            }));

        app.MapPost("/api/session/exchange", async (
            HttpContext context,
            LocalSessionOptions options,
            IDatabaseTargetPolicy targetPolicy) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            var requestHost = context.Request.Host.ToString();
            try
            {
                targetPolicy.ValidateOriginAndHost(origin, requestHost);
            }
            catch (DatabaseIntrospectionException ex)
            {
                return Results.Json(
                    new ApiErrorDto(ex.SafeMessage, ex.Code, Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")),
                    statusCode: ex.StatusCode);
            }

            if (!options.RequiresSessionAuth)
            {
                return Results.NoContent();
            }

            SessionExchangeRequest? body;
            try
            {
                body = await context.Request.ReadFromJsonAsync<SessionExchangeRequest>(context.RequestAborted);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.Json(
                    new ApiErrorDto("Invalid session exchange payload.", "invalid_request", Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!options.IsValidToken(body?.Token))
            {
                return Results.Json(
                    new ApiErrorDto("Invalid launch token.", "invalid_token", Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")),
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            context.Response.Cookies.Append(
                LocalSessionOptions.CookieName,
                options.ComputeSessionCookieValue(),
                new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/api",
                    IsEssential = true,
                });

            return Results.NoContent();
        });
    }
}
