using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Defines the retry behaviour applied when a job fails, including the number of attempts and the delay intervals.
/// </summary>
public sealed class RetryStrategy : ValueObject
{
    /// <summary>
    /// Gets the maximum number of processing attempts, including the first attempt.
    /// </summary>
    public int MaxAttempts { get; private set; } = 3;

    /// <summary>
    /// Gets the ordered array of delay intervals applied between consecutive retry attempts.
    /// </summary>
    public TimeSpan[] RetryIntervals { get; private set; } = [];

    private RetryStrategy() { }

    /// <summary>
    /// Gets the default retry strategy: fixed 15-second delay, 3 attempts, with jitter.
    /// </summary>
    public static RetryStrategy Default => Fixed(TimeSpan.FromSeconds(15), 3, jitter: true);

    /// <summary>
    /// Gets a retry strategy that makes exactly one attempt with no retries.
    /// </summary>
    public static RetryStrategy None => new() { MaxAttempts = 1, RetryIntervals = [] };

    /// <summary>
    /// Creates a retry strategy with a constant delay between each attempt.
    /// </summary>
    /// <param name="delay">The fixed delay between attempts.</param>
    /// <param name="maxAttempts">The maximum number of attempts, including the first.</param>
    /// <param name="jitter">When <see langword="true"/>, applies ±20% random jitter to each interval.</param>
    /// <returns>A new <see cref="RetryStrategy"/> instance.</returns>
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

        List<TimeSpan> intervals = new();

        for (int i = 0; i < maxAttempts; i++)
        {
            intervals.Add(jitter ? ApplyJitter(delay) : delay);
        }

        return new RetryStrategy { MaxAttempts = maxAttempts, RetryIntervals = intervals.ToArray() };
    }

    /// <summary>
    /// Creates a retry strategy with explicit per-attempt delay intervals.
    /// </summary>
    /// <param name="intervals">The ordered sequence of delays. The number of elements determines the maximum attempts.</param>
    /// <returns>A new <see cref="RetryStrategy"/> instance.</returns>
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

    /// <summary>
    /// Creates a retry strategy with exponentially increasing delay intervals.
    /// </summary>
    /// <param name="initialInterval">The delay before the first retry. Must be greater than zero.</param>
    /// <param name="maxAttempts">The maximum number of attempts, including the first.</param>
    /// <param name="exponent">The multiplicative growth factor applied to each successive interval. Must be greater than 1.0.</param>
    /// <param name="maxInterval">Optional ceiling on any single interval. When omitted, intervals are unbounded.</param>
    /// <param name="jitter">When <see langword="true"/>, applies ±20% random jitter to each interval.</param>
    /// <returns>A new <see cref="RetryStrategy"/> instance.</returns>
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

            currentInterval = TimeSpan.FromMilliseconds(Math.Min(nextIntervalMs, maxIntervalValue.TotalMilliseconds));

            if (jitter)
            {
                intervals[i] = ApplyJitter(intervals[i]);
            }
        }
        return new RetryStrategy { MaxAttempts = maxAttempts, RetryIntervals = intervals };
    }

    /// <summary>
    /// Returns <see langword="true"/> if the job should be retried after the given attempt number.
    /// </summary>
    /// <param name="attempt">The current attempt count (1-based).</param>
    /// <returns><see langword="true"/> if a retry should be attempted; otherwise <see langword="false"/>.</returns>
    public bool ShouldRetry(int attempt)
    {
        return attempt < MaxAttempts;
    }

    /// <summary>
    /// Returns the delay interval to apply before the specified retry attempt.
    /// </summary>
    /// <param name="attempt">The attempt number (1-based) for which to retrieve the interval.</param>
    /// <returns>The <see cref="TimeSpan"/> delay to wait before this attempt.</returns>
    public TimeSpan GetRetryInterval(int attempt)
    {
        if (attempt < 1 || attempt > MaxAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), $"Attempt must be between 1 and {MaxAttempts}.");
        }

        return RetryIntervals[attempt - 1];
    }

    /// <summary>
    /// Returns the equality components used to compare two <see cref="RetryStrategy"/> instances.
    /// </summary>
    /// <returns>An enumerable of equality components.</returns>
    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return MaxAttempts;
        foreach (var interval in RetryIntervals)
        {
            yield return interval;
        }
    }

    private static TimeSpan ApplyJitter(TimeSpan interval)
    {
        var jitterFactor = 0.8 + new Random().NextDouble() * 0.4; // Random factor between 0.8 and 1.2
        return TimeSpan.FromMilliseconds(interval.TotalMilliseconds * jitterFactor);
    }
}
