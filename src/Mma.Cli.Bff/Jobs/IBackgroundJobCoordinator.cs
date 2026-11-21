namespace Mma.Cli.Bff.Jobs;

public interface IBackgroundJobCoordinator
{
    string CreateJob(string kind, int? projectId = null);

    void Publish(string jobId, string type, object? payload = null);

    bool TryGetJobStream(string jobId, out IAsyncEnumerable<BffSseEvent> stream);

    IAsyncEnumerable<BffSseEvent> StreamEventsAsync(string jobId, CancellationToken ct = default);
}
