using System.Threading.Channels;
using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal interface IQueuePoller
{
    Task RunAsync(QueueOptions queue, LeaseToken leaseToken, Channel<AtomizerJob> channel, CancellationToken ct);
}

internal class QueuePoller : IQueuePoller
{
    private readonly IAtomizerClock _clock;
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<QueuePoller> _logger;

    private DateTimeOffset _lastStorageCheck;

    public QueuePoller(
        IAtomizerClock clock,
        IAtomizerServiceScopeFactory serviceScopeFactory,
        ILogger<QueuePoller> logger
    )
    {
        _clock = clock;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;

        _lastStorageCheck = _clock.MinValue;
    }

    public async Task RunAsync(
        QueueOptions queue,
        LeaseToken leaseToken,
        Channel<AtomizerJob> channel,
        CancellationToken ct
    )
    {
        var storageCheckInterval = queue.StorageCheckInterval;

        while (!ct.IsCancellationRequested)
        {
            var leasedJobs = new List<AtomizerJob>();

            try
            {
                var now = _clock.UtcNow;
                var itemsInChannel = channel.Reader.CanCount ? channel.Reader.Count : 0;

                if (now - _lastStorageCheck >= storageCheckInterval && itemsInChannel < queue.DegreeOfParallelism)
                {
                    using var scope = _serviceScopeFactory.CreateScope();

                    _lastStorageCheck = now;
                    var leasingScopeFactory = scope.LeasingScopeFactory;

#if NETCOREAPP3_0_OR_GREATER
                    await using var leasingScope = await leasingScopeFactory.CreateScopeAsync(
                        queue.QueueKey,
                        queue.VisibilityTimeout,
                        ct
                    );
#else
                    using var leasingScope = await leasingScopeFactory.CreateScopeAsync(
                        queue.QueueKey,
                        queue.VisibilityTimeout,
                        ct
                    );
#endif
                    var storage = scope.Storage;

                    if (leasingScope.Acquired)
                    {
                        var jobs = await storage.GetDueJobsAsync(queue.QueueKey, now, queue.BatchSize, ct);

                        if (jobs.Count > 0)
                        {
                            _logger.LogDebug("Queue '{Queue}' leasing {JobCount} job(s)", queue.QueueKey, jobs.Count);

                            foreach (var job in jobs)
                            {
                                job.Lease(leaseToken, now, queue.VisibilityTimeout);
                                leasedJobs.Add(job);
                            }

                            await storage.UpdateJobsAsync(leasedJobs, ct);
                        }
                        else
                        {
                            _logger.LogDebug("Queue '{Queue}' found no jobs to lease", queue.QueueKey);
                        }
                    }
                    else
                    {
                        _logger.LogDebug("Failed to acquire processing scope for queue '{Queue}'", queue.QueueKey);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                _logger.LogDebug("Poller for queue '{QueueKey}' cancellation requested", queue.QueueKey);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in poll loop for queue '{QueueKey}'", queue.QueueKey);
            }

            if (leasedJobs.Count > 0)
            {
                foreach (var job in leasedJobs)
                {
                    try
                    {
                        await channel.Writer.WriteAsync(job, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error writing leased job {JobId} to channel for queue '{Queue}'. Will be retried after visibility timeout",
                            job.Id,
                            queue.QueueKey
                        );
                    }
                }

                _logger.LogDebug(
                    "Queue '{Queue}' wrote {JobCount} leased jobs to channel",
                    queue.QueueKey,
                    leasedJobs.Count
                );
            }

            try
            {
                await Task.Delay(queue.TickInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancellation requested, exit the loop
                break;
            }
        }

        _logger.LogDebug("Poller for queue '{QueueKey}' stopped", queue.QueueKey);
    }
}
