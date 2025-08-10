using System;
using Atomizer.Hosting;

namespace Atomizer.Configuration
{
    public sealed class RetryOptions
    {
        public int MaxAttempts { get; set; } = 3;
        public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(3);
        public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
        public AtomizerRetryBackoffStrategy BackoffStrategy { get; set; } =
            AtomizerRetryBackoffStrategy.ExponentialWithJitter;
    }
}
