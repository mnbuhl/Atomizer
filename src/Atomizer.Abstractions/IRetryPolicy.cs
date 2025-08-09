using System;

namespace Atomizer.Abstractions
{
    public interface IRetryPolicy
    {
        bool ShouldRetry(int attempt, Exception error, RetryContext context);
        TimeSpan GetBackoff(int attempt, Exception error, RetryContext context);
        int MaxAttempts { get; }
    }

    public sealed class RetryContext
    {
        public AtomizerQueue Queue { get; set; }
        public AtomizerJob Job { get; set; }

        public RetryContext(AtomizerJob job)
        {
            Job = job;
            Queue = job.Queue;
        }
    }
}
