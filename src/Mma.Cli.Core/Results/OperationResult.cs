namespace Mma.Cli.Core.Results;

public static class ErrorCodes
{
    public const string SOLUTION_NOT_FOUND = nameof(SOLUTION_NOT_FOUND);
    public const string VALIDATION_ERROR = nameof(VALIDATION_ERROR);
    public const string ENTITY_EXISTS = nameof(ENTITY_EXISTS);
    public const string ENTITY_NOT_FOUND = nameof(ENTITY_NOT_FOUND);
    public const string PROPERTY_EXISTS = nameof(PROPERTY_EXISTS);
    public const string PROPERTY_NOT_FOUND = nameof(PROPERTY_NOT_FOUND);
    public const string RELATION_EXISTS = nameof(RELATION_EXISTS);
    public const string RELATION_NOT_FOUND = nameof(RELATION_NOT_FOUND);
    public const string MANIFEST_CONFLICT = nameof(MANIFEST_CONFLICT);
    public const string MANIFEST_INVALID = nameof(MANIFEST_INVALID);
    public const string TEMPLATE_ERROR = nameof(TEMPLATE_ERROR);
    public const string PROCESS_FAILED = nameof(PROCESS_FAILED);
    public const string IO_ERROR = nameof(IO_ERROR);
    public const string HTTP_ERROR = nameof(HTTP_ERROR);
    public const string CANCELLED = nameof(CANCELLED);
}

public sealed record GenerationError(string Code, string Message, string? Details = null);

public class OperationResult
{
    protected OperationResult(
        bool isSuccess,
        GenerationError? error,
        IReadOnlyList<string>? warnings = null)
    {
        IsSuccess = isSuccess;
        Error = error;
        Warnings = warnings ?? Array.Empty<string>();
    }

    public bool IsSuccess { get; }
    public GenerationError? Error { get; }
    public IReadOnlyList<string> Warnings { get; }

    public static OperationResult Success(IReadOnlyList<string>? warnings = null) =>
        new(true, null, warnings);

    public static OperationResult Failure(string code, string message, string? details = null) =>
        new(false, new GenerationError(code, message, details));

    public static OperationResult Failure(GenerationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, error);
    }
}

public sealed class OperationResult<T> : OperationResult
{
    private OperationResult(
        bool isSuccess,
        T? value,
        GenerationError? error,
        IReadOnlyList<string>? warnings = null)
        : base(isSuccess, error, warnings)
    {
        Value = value;
    }

    public T? Value { get; }

    public static OperationResult<T> Success(T value, IReadOnlyList<string>? warnings = null) =>
        new(true, value, null, warnings);

    public static new OperationResult<T> Failure(string code, string message, string? details = null) =>
        new(false, default, new GenerationError(code, message, details));

    public static new OperationResult<T> Failure(GenerationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error);
    }
}
