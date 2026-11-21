namespace Mma.Cli.Bff.Jobs;

public sealed record BffSseEvent(
    string EventId,
    DateTimeOffset Timestamp,
    string Type,
    string JobOrSessionId,
    object? Payload);
