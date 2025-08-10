using System;
using Atomizer.Hosting;

namespace Atomizer.Configuration
{
    public sealed class RetryOptions
    {
        /// <summary>
        /// Gets or sets the maximum number of retry attempts for a job.
        /// <remarks>Default is 3, meaning that a job will be retried up to 3 times before it is considered failed.</remarks>
        /// </summary>
        public int MaxAttempts { get; set; } = 3;

        /// <summary>
        /// Gets or sets the initial backoff time for retries.
        /// <remarks>Default is 3 seconds, meaning that the first retry will be attempted after 3 seconds.</remarks>
        /// </summary>
        public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>
        /// Gets or sets the maximum backoff time for retries.
        /// <remarks>Default is 5 minutes, meaning that the maximum time between retries will not exceed 5 minutes.</remarks>
        /// </summary>
        public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets or sets the backoff strategy to use for retries.
        /// <remarks>Default is ExponentialWithJitter, meaning that the backoff time will increase exponentially with a random jitter to avoid thundering herd problems.</remarks>
        /// </summary>
        public AtomizerRetryBackoffStrategy BackoffStrategy { get; set; } =
            AtomizerRetryBackoffStrategy.ExponentialWithJitter;
    }
}
