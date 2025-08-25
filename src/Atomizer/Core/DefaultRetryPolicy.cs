namespace Atomizer.Core;

public sealed class DefaultRetryPolicy
{
    private readonly Random _rng = new Random();
    private readonly AtomizerRetryContext _context;

    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

    public DefaultRetryPolicy(AtomizerRetryContext context)
    {
        _context = context;
    }

    public bool ShouldRetry(int attempt)
    {
        return attempt < _context.Job.MaxAttempts;
    }

    public TimeSpan GetBackoff(int attempt, Exception error)
    {
        var n = Math.Max(1, attempt);
        var first = InitialBackoff;

        // Add random jitter
        var baseMs = first.TotalMilliseconds * Math.Pow(2, n - 1);
        var factor = 0.8 + _rng.NextDouble() * 0.4; // 0.8x - 1.2x
        var backoff = TimeSpan.FromMilliseconds(baseMs * factor);

        if (backoff > MaxBackoff)
            backoff = MaxBackoff;

        return backoff;
    }
}

public sealed class AtomizerRetryContext
{
    public QueueKey QueueKey { get; set; }
    public AtomizerJob Job { get; set; }

    public AtomizerRetryContext(AtomizerJob job)
    {
        Job = job;
        QueueKey = job.QueueKey;
    }
}
