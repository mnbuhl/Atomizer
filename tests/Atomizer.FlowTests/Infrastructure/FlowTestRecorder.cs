namespace Atomizer.FlowTests.Infrastructure;

internal sealed class FlowTestRecorder
{
    private readonly object _sync = new object();
    private readonly List<FlowAttempt> _attempts = new List<FlowAttempt>();
    private TaskCompletionSource _changed = CreateSignal();
    private int _order;

    public FlowAttempt Record(string key, JobContext context)
    {
        lock (_sync)
        {
            var attempt = new FlowAttempt(
                key,
                context.Job.Id,
                context.Job.QueueKey,
                context.Job.Attempts,
                Interlocked.Increment(ref _order),
                DateTimeOffset.UtcNow
            );
            _attempts.Add(attempt);
            _changed.TrySetResult();
            _changed = CreateSignal();
            return attempt;
        }
    }

    public IReadOnlyList<FlowAttempt> AttemptsFor(string key)
    {
        lock (_sync)
        {
            return _attempts.Where(attempt => attempt.Key == key).ToArray();
        }
    }

    public IReadOnlyList<FlowAttempt> Attempts
    {
        get
        {
            lock (_sync)
            {
                return _attempts.ToArray();
            }
        }
    }

    public async Task WaitForCountAsync(string key, int expectedCount, CancellationToken cancellationToken)
    {
        await WaitUntilAsync(
            attempts => attempts.Count(attempt => attempt.Key == key) >= expectedCount,
            $"Expected at least {expectedCount} attempt(s) for '{key}'.",
            cancellationToken
        );
    }

    public async Task WaitUntilAsync(
        Func<IReadOnlyList<FlowAttempt>, bool> predicate,
        string timeoutMessage,
        CancellationToken cancellationToken
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(FlowTestTimings.WaitTimeout);

        while (true)
        {
            Task changedTask;
            lock (_sync)
            {
                if (predicate(_attempts.ToArray()))
                    return;

                changedTask = _changed.Task;
            }

            try
            {
                await changedTask.WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException(timeoutMessage);
            }
        }
    }

    private static TaskCompletionSource CreateSignal() =>
        new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record FlowAttempt(
    string Key,
    Guid JobId,
    QueueKey QueueKey,
    int Attempt,
    int Order,
    DateTimeOffset StartedAt
);
