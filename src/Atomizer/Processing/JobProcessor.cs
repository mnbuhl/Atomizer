using System;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Hosting;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal sealed class JobProcessor
    {
        private readonly QueueOptions _queue;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerJobDispatcher _dispatcher;
        private readonly IAtomizerStorage _storage;
        private readonly ILogger _logger;

        public JobProcessor(
            QueueOptions queue,
            IAtomizerClock clock,
            IAtomizerJobDispatcher dispatcher,
            IAtomizerStorage storage,
            ILogger logger
        )
        {
            _queue = queue;
            _clock = clock;
            _dispatcher = dispatcher;
            _storage = storage;
            _logger = logger;
        }

        public async Task ProcessAsync(AtomizerJob job, CancellationToken ct)
        {
            var now = _clock.UtcNow;

            try
            {
                job.Attempts++;

                _logger.LogDebug(
                    "Executing job {JobId} (attempt {Attempt}) on '{Queue}'",
                    job.Id,
                    job.Attempts,
                    _queue.QueueKey
                );

                await _dispatcher.DispatchAsync(job, ct);

                job.MarkAsCompleted(now);

                await _storage.UpdateAsync(job, ct);

                _logger.LogInformation(
                    "Job {JobId} succeeded in {Ms}ms on '{Queue}'",
                    job.Id,
                    (int)(_clock.UtcNow - now).TotalMilliseconds,
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Operation cancelled while processing job {JobId} on '{Queue}'",
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
            try
            {
                var retryCtx = new AtomizerRetryContext(job);
                var retryPolicy = new DefaultRetryPolicy(retryCtx);

                var now = _clock.UtcNow;

                job.Errors.Add(AtomizerJobError.Create(job.Id, now, job.Attempts, ex, job.LeaseToken?.InstanceId));

                if (retryPolicy.ShouldRetry(job.Attempts))
                {
                    var delay = retryPolicy.GetBackoff(job.Attempts, ex);
                    var nextVisible = now + delay;

                    job.Retry(nextVisible, now);

                    _logger.LogWarning(
                        "Job {JobId} failed (attempt {Attempt}) on '{Queue}', retrying after {Delay}ms",
                        job.Id,
                        job.Attempts,
                        _queue.QueueKey,
                        delay.TotalMilliseconds
                    );
                }
                else
                {
                    job.MarkAsFailed(now);

                    _logger.LogError(
                        "Job {JobId} exhausted retries and was marked as failed on '{Queue}'",
                        job.Id,
                        _queue.QueueKey
                    );
                }

                await _storage.UpdateAsync(job, ct);
            }
            catch (Exception jobFailureEx)
            {
                _logger.LogError(
                    jobFailureEx,
                    "Error while handling failure of job {JobId} on '{Queue}' on attempt {Attempt}",
                    job.Id,
                    _queue.QueueKey,
                    job.Attempts
                );
            }
        }
    }
}
