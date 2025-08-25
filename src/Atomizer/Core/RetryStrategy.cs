using Atomizer.Exceptions;

namespace Atomizer;

public sealed class RetryStrategy
{
    public int MaxAttempts { get; private set; } = 3;
    public TimeSpan[] RetryIntervals { get; private set; } = [];

    public static RetryStrategy Default => Fixed(TimeSpan.FromSeconds(15), 3, jitter: true);

    public static RetryStrategy None => new() { MaxAttempts = 1, RetryIntervals = [] };

    public static RetryStrategy Fixed(TimeSpan delay, int maxAttempts, bool jitter = false)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new InvalidRetryStrategyException("Delay cannot be negative.", nameof(delay));
        }

        if (maxAttempts < 1)
        {
            throw new InvalidRetryStrategyException("MaxAttempts must be at least 1.", nameof(maxAttempts));
        }

        var jitteredDelay = jitter
            ? TimeSpan.FromMilliseconds(delay.TotalMilliseconds * (0.8 + new Random().NextDouble() * 0.4))
            : delay;

        return new RetryStrategy
        {
            MaxAttempts = maxAttempts,
            RetryIntervals = Enumerable.Repeat(jitteredDelay, maxAttempts).ToArray(),
        };
    }

    public static RetryStrategy Intervals(IEnumerable<TimeSpan> intervals)
    {
        var intervalsArray = intervals.ToArray();

        if (intervalsArray is null || intervalsArray.Length == 0)
        {
            throw new InvalidRetryStrategyException("Intervals cannot be null or empty.", nameof(intervals));
        }

        if (intervalsArray.Any(i => i < TimeSpan.Zero))
        {
            throw new InvalidRetryStrategyException("Intervals cannot contain negative values.", nameof(intervals));
        }

        return new RetryStrategy { MaxAttempts = intervalsArray.Length, RetryIntervals = intervalsArray };
    }

    public static RetryStrategy Exponential(
        TimeSpan initialInterval,
        int maxAttempts,
        double exponent = 2.0,
        TimeSpan? maxInterval = null,
        bool jitter = false
    )
    {
        if (initialInterval <= TimeSpan.Zero)
        {
            throw new InvalidRetryStrategyException(
                "InitialInterval must be greater than zero for exponential backoff.",
                nameof(initialInterval)
            );
        }

        if (maxAttempts < 1)
        {
            throw new InvalidRetryStrategyException("MaxAttempts must be at least 1.", nameof(maxAttempts));
        }

        if (exponent <= 1.0)
        {
            throw new InvalidRetryStrategyException("Exponent must be greater than 1.0.", nameof(exponent));
        }

        if (maxInterval.HasValue && maxInterval.Value <= TimeSpan.Zero)
        {
            throw new InvalidRetryStrategyException(
                "MaxInterval must be greater than zero if specified.",
                nameof(maxInterval)
            );
        }

        var intervals = new TimeSpan[maxAttempts];
        var currentInterval = initialInterval;
        var maxIntervalValue = maxInterval ?? TimeSpan.MaxValue;
        for (var i = 0; i < maxAttempts; i++)
        {
            intervals[i] = currentInterval;
            var nextIntervalMs = currentInterval.TotalMilliseconds * exponent;

            if (jitter)
            {
                var factor = 0.8 + new Random().NextDouble() * 0.4; // 0.8x - 1.2x
                nextIntervalMs *= factor;
            }

            currentInterval = TimeSpan.FromMilliseconds(Math.Min(nextIntervalMs, maxIntervalValue.TotalMilliseconds));
        }
        return new RetryStrategy { MaxAttempts = maxAttempts, RetryIntervals = intervals };
    }
}
