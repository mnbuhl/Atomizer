using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal interface IJobProcessor
{
    Task ProcessAsync(AtomizerJob job, CancellationToken ct);
}

internal sealed class JobProcessor : IJobProcessor
{
    private readonly IAtomizerClock _clock;
    private readonly IAtomizerJobDispatcher _dispatcher;
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger _logger;

    public JobProcessor(
        IAtomizerClock clock,
        IAtomizerJobDispatcher dispatcher,
        IAtomizerServiceScopeFactory serviceScopeFactory,
        ILogger logger
    )
    {
        _clock = clock;
        _dispatcher = dispatcher;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task ProcessAsync(AtomizerJob job, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        try
        {
            job.Attempt();

            _logger.LogDebug(
                "Executing job {JobId} (attempt {Attempt}) on '{Queue}'",
                job.Id,
                job.Attempts,
                job.QueueKey
            );

            await _dispatcher.DispatchAsync(job, ct);

            job.MarkAsCompleted(now);

            using var scope = _serviceScopeFactory.CreateScope();
            var storage = scope.Storage;

            await storage.UpdateJobsAsync(new[] { job }, ct);

            _logger.LogInformation(
                "Job {JobId} succeeded in {Ms}ms on '{Queue}'",
                job.Id,
                (int)(_clock.UtcNow - now).TotalMilliseconds,
                job.QueueKey
            );
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning("Operation cancelled while processing job {JobId} on '{Queue}'", job.Id, job.QueueKey);
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
            var now = _clock.UtcNow;

            job.Errors.Add(AtomizerJobError.Create(job.Id, now, job.Attempts, ex, job.LeaseToken?.InstanceId));

            if (job.RetryStrategy.ShouldRetry(job.Attempts))
            {
                var delay = job.RetryStrategy.GetRetryInterval(job.Attempts);
                var nextVisible = now + delay;

                job.Reschedule(nextVisible, now);

                _logger.LogWarning(
                    "Job {JobId} failed (attempt {Attempt}) on '{Queue}', retrying after {Delay}ms",
                    job.Id,
                    job.Attempts,
                    job.QueueKey,
                    delay.TotalMilliseconds
                );
            }
            else
            {
                job.MarkAsFailed(now);

                _logger.LogError(
                    "Job {JobId} exhausted retries and was marked as failed on '{Queue}'",
                    job.Id,
                    job.QueueKey
                );
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var storage = scope.Storage;

            await storage.UpdateJobsAsync(new[] { job }, ct);
        }
        catch (Exception jobFailureEx)
        {
            _logger.LogError(
                jobFailureEx,
                "Error while handling failure of job {JobId} on '{Queue}' on attempt {Attempt}",
                job.Id,
                job.QueueKey,
                job.Attempts
            );
        }
    }
}
