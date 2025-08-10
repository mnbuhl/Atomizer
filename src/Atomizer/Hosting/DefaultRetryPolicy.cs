using System;
using Atomizer.Abstractions;
using Atomizer.Configuration;

namespace Atomizer.Hosting
{
    internal sealed class DefaultRetryPolicy : IRetryPolicy
    {
        public RetryOptions Options { get; set; } = new RetryOptions();
        private readonly Random _rng = new Random();

        public int MaxAttempts => Options.MaxAttempts;

        public bool ShouldRetry(int attempt, Exception error, RetryContext context)
        {
            return attempt < Options.MaxAttempts;
        }

        public TimeSpan GetBackoff(int attempt, Exception error, RetryContext context)
        {
            var n = Math.Max(1, attempt);
            var first = Options.InitialBackoff;

            TimeSpan backoff;
            switch (Options.BackoffStrategy)
            {
                case RetryBackoffStrategy.Fixed:
                    backoff = first;
                    break;
                case RetryBackoffStrategy.Exponential:
                    backoff = TimeSpan.FromMilliseconds(first.TotalMilliseconds * Math.Pow(2, n - 1));
                    break;
                case RetryBackoffStrategy.ExponentialWithJitter:
                    var baseMs = first.TotalMilliseconds * Math.Pow(2, n - 1);
                    var factor = 0.8 + _rng.NextDouble() * 0.4; // 0.8x - 1.2x
                    backoff = TimeSpan.FromMilliseconds(baseMs * factor);
                    break;
                default:
                    backoff = first;
                    break;
            }

            if (backoff > Options.MaxBackoff)
                backoff = Options.MaxBackoff;

            return backoff;
        }
    }
}
