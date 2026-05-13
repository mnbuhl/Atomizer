using System.Threading.Channels;
using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal interface IQueuePoller
{
    Task RunAsync(QueueOptions queue, LeaseToken leaseToken, Channel<JobBatch> channel, CancellationToken ct);
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
        Channel<JobBatch> channel,
        CancellationToken ct
    )
    {
        var storageCheckInterval = queue.StorageCheckInterval;

        while (!ct.IsCancellationRequested)
        {
            List<AtomizerJob> leasedJobs = [];

            try
            {
                var now = _clock.UtcNow;
                var itemsInChannel = channel.Reader.CanCount ? channel.Reader.Count : 0;

                if (now - _lastStorageCheck >= storageCheckInterval && itemsInChannel < queue.DegreeOfParallelism)
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    _lastStorageCheck = now;
                    var storage = scope.Storage;

                    leasedJobs =
                        await storage.ExecuteInLeaseAsync(
                            queue.QueueKey,
                            async innerCt =>
                            {
                                var jobs = await storage.GetDueJobsAsync(queue.QueueKey, now, queue.BatchSize, innerCt);
                                var acquired = new List<AtomizerJob>();

                                if (jobs.Count > 0)
                                {
                                    _logger.LogDebug(
                                        "Queue '{Queue}' leasing {JobCount} job(s)",
                                        queue.QueueKey,
                                        jobs.Count
                                    );

                                    foreach (var job in jobs)
                                    {
                                        job.Lease(leaseToken, now, queue.VisibilityTimeout);
                                        acquired.Add(job);
                                    }

                                    await storage.UpdateJobsAsync(acquired, innerCt);
                                }
                                else
                                {
                                    _logger.LogDebug("Queue '{Queue}' found no jobs to lease", queue.QueueKey);
                                }

                                return acquired;
                            },
                            ct
                        ) ?? [];
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
                var batches = CreateBatches(leasedJobs);
                foreach (var batch in batches)
                {
                    try
                    {
                        await channel.Writer.WriteAsync(batch, ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Error writing leased job batch starting with {JobId} to channel for queue '{Queue}'. Will be retried after visibility timeout",
                            batch.FirstJob.Id,
                            queue.QueueKey
                        );
                    }
                }

                _logger.LogDebug(
                    "Queue '{Queue}' wrote {JobCount} leased jobs in {BatchCount} batch(es) to channel",
                    queue.QueueKey,
                    leasedJobs.Count,
                    batches.Count
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

    private static List<JobBatch> CreateBatches(IReadOnlyList<AtomizerJob> jobs)
    {
        var batches = new List<JobBatch>();

        foreach (var job in jobs.Where(j => j.PartitionKey == null))
        {
            batches.Add(new JobBatch(new[] { job }));
        }

        var partitionedBatches = jobs.Where(j => j.PartitionKey != null)
            .GroupBy(j => new { QueueKey = j.QueueKey.Key, PartitionKey = j.PartitionKey!.Key })
            .Select(group => new JobBatch(
                group
                    .OrderBy(j => j.SequenceNumber ?? long.MaxValue)
                    .ThenBy(j => j.ScheduledAt)
                    .ThenBy(j => j.CreatedAt)
                    .ToList()
            ));

        batches.AddRange(partitionedBatches);

        return batches
            .OrderBy(batch => batch.FirstJob.ScheduledAt)
            .ThenBy(batch => batch.FirstJob.CreatedAt)
            .ThenBy(batch => batch.FirstJob.SequenceNumber ?? long.MaxValue)
            .ToList();
    }
}
