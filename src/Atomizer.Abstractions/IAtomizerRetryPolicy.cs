using System;

namespace Atomizer.Abstractions
{
    public interface IAtomizerRetryPolicy
    {
        bool ShouldRetry(int attempt, Exception error, AtomizerRetryContext context);
        TimeSpan GetBackoff(int attempt, Exception error, AtomizerRetryContext context);
        int MaxAttempts { get; }
    }

    public enum AtomizerRetryBackoffStrategy
    {
        Fixed,
        Exponential,
        ExponentialWithJitter,
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
}
