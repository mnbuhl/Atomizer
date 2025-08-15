using System;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Hosting;
using Atomizer.Models;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal sealed class JobProcessor
    {
        private readonly QueueOptions _queue;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerJobDispatcher _dispatcher;
        private readonly DefaultRetryPolicy _retryPolicy;
        private readonly IAtomizerJobStorage _storage;
        private readonly ILogger _logger;
        private readonly string _leaseToken;

        public JobProcessor(
            QueueOptions queue,
            IAtomizerClock clock,
            IAtomizerJobDispatcher dispatcher,
            DefaultRetryPolicy retryPolicy,
            IAtomizerJobStorage storage,
            ILogger logger,
            string leaseToken
        )
        {
            _queue = queue;
            _clock = clock;
            _dispatcher = dispatcher;
            _retryPolicy = retryPolicy;
            _storage = storage;
            _logger = logger;
            _leaseToken = leaseToken;
        }

        public async Task ProcessAsync(AtomizerJob job, CancellationToken ct)
        {
            var swStart = _clock.UtcNow;

            try
            {
                _logger.LogDebug(
                    "Executing job {JobId} (attempt {Attempt}) on '{Queue}'",
                    job.Id,
                    job.Attempt,
                    _queue.QueueKey
                );

                await _dispatcher.DispatchAsync(job, ct);

                await _storage.MarkCompletedAsync(job.Id, _leaseToken, _clock.UtcNow, ct);

                _logger.LogInformation(
                    "Job {JobId} succeeded in {Ms}ms on '{Queue}'",
                    job.Id,
                    (int)(_clock.UtcNow - swStart).TotalMilliseconds,
                    _queue.QueueKey
                );
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Cancellation requested while processing job {JobId} on '{Queue}'",
                    job.Id,
                    _queue.QueueKey
                );
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(job, ex, ct);
            }
        }

        private async Task HandleFailureAsync(AtomizerJob job, Exception ex, CancellationToken ct)
        {
            var attempt = job.Attempt;

            try
            {
                var retryCtx = new AtomizerRetryContext(job);

                if (_retryPolicy.ShouldRetry(attempt, ex, retryCtx))
                {
                    var delay = _retryPolicy.GetBackoff(attempt, ex, retryCtx);
                    var nextVisible = _clock.UtcNow + delay;

                    await _storage.RescheduleAsync(job.Id, _leaseToken, attempt + 1, nextVisible, ct);

                    _logger.LogWarning(
                        "Job {JobId} failed (attempt {Attempt}) on '{Queue}', retrying after {Delay}ms",
                        job.Id,
                        attempt,
                        _queue.QueueKey,
                        (int)delay.TotalMilliseconds
                    );
                }
                else
                {
                    await _storage.MarkFailedAsync(job.Id, _leaseToken, ex, _clock.UtcNow, ct);

                    _logger.LogError(
                        "Job {JobId} exhausted retries and was dead-lettered on '{Queue}'",
                        job.Id,
                        _queue.QueueKey
                    );
                }
            }
            catch (Exception jobFailureEx)
            {
                _logger.LogError(
                    jobFailureEx,
                    "Error while handling failure of job {JobId} on '{Queue}' on attempt {Attempt}",
                    job.Id,
                    _queue.QueueKey,
                    attempt
                );
            }
        }
    }
}
