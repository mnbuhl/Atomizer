using System;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Models;

namespace Atomizer.Hosting
{
    public sealed class DefaultRetryPolicy
    {
        private readonly RetryOptions _options;
        private readonly Random _rng = new Random();

        public DefaultRetryPolicy(RetryOptions options)
        {
            _options = options;
        }

        public int MaxAttempts => _options.MaxAttempts;

        public bool ShouldRetry(int attempt, Exception error, AtomizerRetryContext context)
        {
            return attempt < _options.MaxAttempts;
        }

        public TimeSpan GetBackoff(int attempt, Exception error, AtomizerRetryContext context)
        {
            var n = Math.Max(1, attempt);
            var first = _options.InitialBackoff;

            TimeSpan backoff;
            switch (_options.BackoffStrategy)
            {
                case AtomizerRetryBackoffStrategy.Fixed:
                    backoff = first;
                    break;
                case AtomizerRetryBackoffStrategy.Exponential:
                    backoff = TimeSpan.FromMilliseconds(first.TotalMilliseconds * Math.Pow(2, n - 1));
                    break;
                case AtomizerRetryBackoffStrategy.ExponentialWithJitter:
                    var baseMs = first.TotalMilliseconds * Math.Pow(2, n - 1);
                    var factor = 0.8 + _rng.NextDouble() * 0.4; // 0.8x - 1.2x
                    backoff = TimeSpan.FromMilliseconds(baseMs * factor);
                    break;
                default:
                    backoff = first;
                    break;
            }

            if (backoff > _options.MaxBackoff)
                backoff = _options.MaxBackoff;

            return backoff;
        }
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
