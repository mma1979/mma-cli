using System.Diagnostics;

namespace Mma.Cli.Bff.Security;

public sealed class LocalSessionAuthMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        LocalSessionOptions sessionOptions,
        IDatabaseTargetPolicy targetPolicy)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/api/session/exchange", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (!sessionOptions.RequiresSessionAuth)
        {
            await next(context);
            return;
        }

        var origin = context.Request.Headers.Origin.ToString();
        var requestHost = context.Request.Host.ToString();

        // Enforce origin/host when Origin is supplied or on any state-changing HTTP method.
        if (!string.IsNullOrWhiteSpace(origin) || !HttpMethods.IsGet(context.Request.Method))
        {
            try
            {
                targetPolicy.ValidateOriginAndHost(origin, requestHost);
            }
            catch (DatabaseIntrospectionException ex)
            {
                context.Response.StatusCode = ex.StatusCode;
                await context.Response.WriteAsJsonAsync(
                    new ApiErrorDto(ex.SafeMessage, ex.Code, Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")),
                    context.RequestAborted);
                return;
            }
        }

        var cookie = context.Request.Cookies[LocalSessionOptions.CookieName];
        if (!sessionOptions.IsValidSessionCookie(cookie))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(
                new ApiErrorDto("Authentication required.", "unauthorized", Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")),
                context.RequestAborted);
            return;
        }

        await next(context);
    }
}
