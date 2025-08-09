using System;

namespace Atomizer.Abstractions
{
    public interface IRetryPolicy
    {
        bool ShouldRetry(int attempt, Exception error, RetryContext context);
        TimeSpan GetBackoff(int attempt, Exception error, RetryContext context);
        int MaxAttempts { get; }
    }

    public enum RetryBackoffStrategy
    {
        Fixed,
        Exponential,
        ExponentialWithJitter,
    }

    public sealed class RetryContext
    {
        public QueueKey QueueKey { get; set; }
        public AtomizerJob Job { get; set; }

        public RetryContext(AtomizerJob job)
        {
            Job = job;
            QueueKey = job.QueueKey;
        }
    }
}
