using System;
using Atomizer.Abstractions;
using Atomizer.Configuration;

namespace Atomizer.Hosting
{
    public class DefaultRetryPolicy : IRetryPolicy
    {
        private readonly RetryOptions _opts;
        private readonly Random _rng = new Random();

        public DefaultRetryPolicy(RetryOptions opts)
        {
            _opts = opts;
        }

        public int MaxAttempts => _opts.MaxAttempts;

        public bool ShouldRetry(int attempt, Exception error, RetryContext context)
        {
            return attempt < _opts.MaxAttempts;
        }

        public TimeSpan GetBackoff(int attempt, Exception error, RetryContext context)
        {
            var n = Math.Max(1, attempt);
            var first = _opts.InitialBackoff;

            TimeSpan backoff;
            switch (_opts.BackoffStrategy)
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

            if (backoff > _opts.MaxBackoff)
                backoff = _opts.MaxBackoff;

            return backoff;
        }
    }
}
