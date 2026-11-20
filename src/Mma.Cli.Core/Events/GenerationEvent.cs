namespace Mma.Cli.Core.Events;

public enum GenerationStage
{
    Starting,
    Validating,
    Scaffolding,
    Updating,
    ProcessOutput,
    Completed,
    Warning,
    Failed
}

public sealed record GenerationEvent(
    GenerationStage Stage,
    string Message,
    string? FilePath = null,
    DateTimeOffset Timestamp = default)
{
    public DateTimeOffset Timestamp { get; init; } = Timestamp == default ? DateTimeOffset.UtcNow : Timestamp;

    public static GenerationEvent Create(
        GenerationStage stage,
        string message,
        string? filePath = null) =>
        new(stage, message, filePath, DateTimeOffset.UtcNow);
}
