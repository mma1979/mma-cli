using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Mma.Cli.Bff.Jobs;

public sealed class BackgroundJobCoordinator : IBackgroundJobCoordinator
{
    private const int MaxHistoryEventsPerJob = 512;
    private const int MaxCompletedJobsRetained = 256;

    private readonly ConcurrentDictionary<string, JobChannelState> _jobs = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _completedOrder = new();

    public string CreateJob(string kind, int? projectId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        var jobId = $"job_{Guid.NewGuid():N}";
        var state = new JobChannelState(jobId, kind, projectId);
        _jobs[jobId] = state;
        return jobId;
    }

    public void Publish(string jobId, string type, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        if (!_jobs.TryGetValue(jobId, out var state))
        {
            return;
        }

        var isTerminal = string.Equals(type, "done", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "error", StringComparison.OrdinalIgnoreCase);

        var evt = new BffSseEvent(
            $"evt_{Guid.NewGuid():N}"[..20],
            DateTimeOffset.UtcNow,
            type,
            jobId,
            payload);

        var completedNow = state.Publish(evt, isTerminal);
        if (completedNow)
        {
            _completedOrder.Enqueue(jobId);
            PruneCompletedJobs();
        }
    }

    public bool TryGetJobStream(string jobId, out IAsyncEnumerable<BffSseEvent> stream)
    {
        if (string.IsNullOrWhiteSpace(jobId) || !_jobs.TryGetValue(jobId, out var state))
        {
            stream = AsyncEnumerable.Empty<BffSseEvent>();
            return false;
        }

        stream = ReadStateEventsAsync(state, CancellationToken.None);
        return true;
    }

    public IAsyncEnumerable<BffSseEvent> StreamEventsAsync(string jobId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobId) || !_jobs.TryGetValue(jobId, out var state))
        {
            return AsyncEnumerable.Empty<BffSseEvent>();
        }

        return ReadStateEventsAsync(state, ct);
    }

    private static async IAsyncEnumerable<BffSseEvent> ReadStateEventsAsync(
        JobChannelState state,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var (snapshot, subscriber) = state.Subscribe();

        try
        {
            foreach (var past in snapshot)
            {
                ct.ThrowIfCancellationRequested();
                yield return past;
            }

            if (subscriber is null)
            {
                yield break;
            }

            await foreach (var live in subscriber.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return live;
            }
        }
        finally
        {
            if (subscriber is not null)
            {
                state.Unsubscribe(subscriber);
                subscriber.Writer.TryComplete();
            }
        }
    }

    private void PruneCompletedJobs()
    {
        while (_completedOrder.Count > MaxCompletedJobsRetained && _completedOrder.TryDequeue(out var oldJobId))
        {
            _jobs.TryRemove(oldJobId, out _);
        }
    }

    private sealed class JobChannelState(string jobId, string kind, int? projectId)
    {
        private readonly Lock _gate = new();
        private readonly Queue<BffSseEvent> _history = new();
        private readonly List<Channel<BffSseEvent>> _subscribers = [];
        private bool _completed;

        public string JobId { get; } = jobId;
        public string Kind { get; } = kind;
        public int? ProjectId { get; } = projectId;

        public bool Publish(BffSseEvent evt, bool isTerminal)
        {
            List<Channel<BffSseEvent>>? targets = null;
            var becameCompleted = false;

            lock (_gate)
            {
                if (_completed)
                {
                    return false;
                }

                _history.Enqueue(evt);
                while (_history.Count > MaxHistoryEventsPerJob)
                {
                    _history.Dequeue();
                }

                if (_subscribers.Count > 0)
                {
                    targets = [.. _subscribers];
                }

                if (isTerminal)
                {
                    _completed = true;
                    becameCompleted = true;
                    _subscribers.Clear();
                }
            }

            if (targets is not null)
            {
                foreach (var channel in targets)
                {
                    channel.Writer.TryWrite(evt);
                    if (isTerminal)
                    {
                        channel.Writer.TryComplete();
                    }
                }
            }

            return becameCompleted;
        }

        public (BffSseEvent[] Snapshot, Channel<BffSseEvent>? Subscriber) Subscribe()
        {
            lock (_gate)
            {
                var snapshot = _history.ToArray();
                if (_completed)
                {
                    return (snapshot, null);
                }

                var channel = Channel.CreateUnbounded<BffSseEvent>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
                _subscribers.Add(channel);
                return (snapshot, channel);
            }
        }

        public void Unsubscribe(Channel<BffSseEvent> channel)
        {
            lock (_gate)
            {
                _subscribers.Remove(channel);
            }
        }
    }
}
