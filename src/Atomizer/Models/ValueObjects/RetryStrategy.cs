using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Defines the retry behavior for a failed job, including the number of attempts and per-attempt delays.
/// </summary>
public sealed class RetryStrategy : ValueObject
{
    /// <summary>
    /// Gets the maximum number of attempts before the job is marked as failed.
    /// </summary>
    public int MaxAttempts { get; private set; } = 3;

    /// <summary>
    /// Gets the delay to apply before each retry attempt.
    /// </summary>
    public TimeSpan[] RetryIntervals { get; private set; } = [];

    private RetryStrategy() { }

    /// <summary>
    /// Gets the default retry strategy: 3 attempts with 15-second fixed delays and plus-or-minus 20% jitter.
    /// </summary>
    public static RetryStrategy Default => Fixed(TimeSpan.FromSeconds(15), 3, jitter: true);

    /// <summary>
    /// Gets a strategy that makes a single attempt with no retries.
    /// </summary>
    public static RetryStrategy None => new() { MaxAttempts = 1, RetryIntervals = [TimeSpan.Zero] };

    /// <summary>
    /// Creates a retry strategy with a constant delay between attempts, optionally applying random jitter.
    /// </summary>
    /// <param name="delay">The fixed delay between attempts. Must be non-negative.</param>
    /// <param name="maxAttempts">The maximum number of attempts. Must be at least 1.</param>
    /// <param name="jitter">When true, applies plus-or-minus 20% random jitter to each delay.</param>
    /// <returns>A new <see cref="RetryStrategy"/> with constant intervals.</returns>
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
    /// <param name="intervals">The ordered list of delays, one per retry attempt. Must be non-empty and contain no negative values.</param>
    /// <returns>A new <see cref="RetryStrategy"/> using the provided intervals. The number of attempts equals the number of intervals.</returns>
    public static RetryStrategy Intervals(IEnumerable<TimeSpan> intervals)
    {
        var intervalsArray = intervals.ToArray();

        if (intervalsArray.Length == 0)
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
    /// Creates a retry strategy with exponentially increasing delays between attempts.
    /// </summary>
    /// <param name="initialInterval">The delay before the first retry. Must be greater than zero.</param>
    /// <param name="maxAttempts">The maximum number of attempts. Must be at least 1.</param>
    /// <param name="exponent">The growth factor applied to each successive interval. Must be greater than 1.0.</param>
    /// <param name="maxInterval">Optional upper bound on any single interval. Must be greater than zero if specified.</param>
    /// <param name="jitter">When true, applies plus-or-minus 20% random jitter to each computed interval.</param>
    /// <returns>A new <see cref="RetryStrategy"/> with exponentially increasing intervals.</returns>
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
    /// Returns true if another attempt should be made after the given attempt number.
    /// </summary>
    /// <param name="attempt">The zero-based attempt count already made.</param>
    /// <returns>true when attempt is less than MaxAttempts.</returns>
    public bool ShouldRetry(int attempt)
    {
        return attempt < MaxAttempts;
    }

    /// <summary>
    /// Returns the delay to apply before the specified attempt.
    /// </summary>
    /// <param name="attempt">The one-based attempt number (1 = first retry).</param>
    /// <returns>The TimeSpan delay for the given attempt.</returns>
    public TimeSpan GetRetryInterval(int attempt)
    {
        if (attempt < 1 || attempt > MaxAttempts)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), $"Attempt must be between 1 and {MaxAttempts}.");
        }

        return RetryIntervals[attempt - 1];
    }

    /// <summary>
    /// Returns the MaxAttempts and all retry intervals as equality components.
    /// </summary>
    /// <returns>An enumerable of equality-defining values.</returns>
    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return MaxAttempts;
        foreach (var interval in RetryIntervals)
        {
            yield return interval;
        }
    }

#if NET6_0_OR_GREATER
    private static TimeSpan ApplyJitter(TimeSpan interval)
    {
        var jitterFactor = 0.8 + Random.Shared.NextDouble() * 0.4; // Random factor between 0.8 and 1.2
        return TimeSpan.FromMilliseconds(interval.TotalMilliseconds * jitterFactor);
    }
#else
    private static readonly ThreadLocal<Random> _random = new ThreadLocal<Random>(() =>
        new Random(Guid.NewGuid().GetHashCode())
    );

    private static TimeSpan ApplyJitter(TimeSpan interval)
    {
        var jitterFactor = 0.8 + _random.Value!.NextDouble() * 0.4; // Random factor between 0.8 and 1.2
        return TimeSpan.FromMilliseconds(interval.TotalMilliseconds * jitterFactor);
    }
#endif
}
