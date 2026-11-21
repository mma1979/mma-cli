using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Mma.Cli.Core.Results;

namespace Mma.Cli.Bff.Services;

public static class BffErrorCodes
{
    public const string INVALID_HOST = nameof(INVALID_HOST);
    public const string ORIGIN_NOT_ALLOWED = nameof(ORIGIN_NOT_ALLOWED);
    public const string UNAUTHORIZED = nameof(UNAUTHORIZED);
    public const string AUTH_TOKEN_CONSUMED = nameof(AUTH_TOKEN_CONSUMED);
    public const string AUTH_TOKEN_INVALID = nameof(AUTH_TOKEN_INVALID);
    public const string CSRF_INVALID = nameof(CSRF_INVALID);
    public const string PATH_OUTSIDE_ALLOWLIST = nameof(PATH_OUTSIDE_ALLOWLIST);
    public const string JOB_NOT_FOUND = nameof(JOB_NOT_FOUND);
}

public static class BffProblemDetails
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static int MapStatusCode(string? errorCode) => errorCode switch
    {
        BffErrorCodes.UNAUTHORIZED or BffErrorCodes.AUTH_TOKEN_CONSUMED or BffErrorCodes.AUTH_TOKEN_INVALID
            => StatusCodes.Status401Unauthorized,
        BffErrorCodes.INVALID_HOST or BffErrorCodes.ORIGIN_NOT_ALLOWED or BffErrorCodes.CSRF_INVALID or BffErrorCodes.PATH_OUTSIDE_ALLOWLIST
            => StatusCodes.Status403Forbidden,
        ErrorCodes.SOLUTION_NOT_FOUND or ErrorCodes.ENTITY_NOT_FOUND or ErrorCodes.PROPERTY_NOT_FOUND or ErrorCodes.RELATION_NOT_FOUND or BffErrorCodes.JOB_NOT_FOUND
            => StatusCodes.Status404NotFound,
        ErrorCodes.MANIFEST_CONFLICT
            => StatusCodes.Status409Conflict,
        ErrorCodes.PROCESS_FAILED
            => StatusCodes.Status422UnprocessableEntity,
        ErrorCodes.VALIDATION_ERROR or ErrorCodes.TEMPLATE_ERROR or ErrorCodes.ENTITY_EXISTS or ErrorCodes.PROPERTY_EXISTS or ErrorCodes.RELATION_EXISTS or ErrorCodes.MANIFEST_INVALID or ErrorCodes.CANCELLED
            => StatusCodes.Status400BadRequest,
        ErrorCodes.IO_ERROR or ErrorCodes.HTTP_ERROR
            => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError
    };

    public static ProblemDetails Create(
        string errorCode,
        string detail,
        string? title = null,
        string? instance = null)
    {
        var status = MapStatusCode(errorCode);
        var problem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc9457",
            Title = title ?? GetDefaultTitle(status),
            Status = status,
            Detail = detail,
            Instance = instance
        };
        problem.Extensions["errorCode"] = errorCode;
        return problem;
    }

    public static IResult ToResult(string errorCode, string detail, string? title = null)
    {
        var problem = Create(errorCode, detail, title);
        return Results.Problem(
            detail: problem.Detail,
            instance: problem.Instance,
            statusCode: problem.Status,
            title: problem.Title,
            type: problem.Type,
            extensions: problem.Extensions);
    }

    public static IResult ToResult(GenerationError? error, string fallbackDetail)
    {
        if (error is null)
        {
            return ToResult(ErrorCodes.IO_ERROR, fallbackDetail);
        }

        return ToResult(error.Code, error.Message);
    }

    public static async Task WriteAsync(
        HttpContext context,
        string errorCode,
        string detail,
        string? title = null)
    {
        var problem = Create(errorCode, detail, title, context.Request.Path);
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem, SerializerOptions, "application/problem+json", context.RequestAborted)
            .ConfigureAwait(false);
    }

    private static string GetDefaultTitle(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        _ => "Internal Server Error"
    };
}
