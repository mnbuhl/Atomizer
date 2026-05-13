using System.Threading.Channels;
using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal interface IJobWorker
{
    Task RunAsync(ChannelReader<JobBatch> reader, CancellationToken ioToken, CancellationToken executionToken);
}

internal sealed class JobWorker : IJobWorker
{
    private readonly WorkerId _workerId;
    private readonly IJobProcessorFactory _jobProcessorFactory;
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory;
    private readonly IAtomizerClock _clock;
    private readonly ILogger _logger;

    private int _readRetries = 0;
    private const int MaxReadAttempts = 5;

    public JobWorker(
        WorkerId workerId,
        IJobProcessorFactory jobProcessorFactory,
        IAtomizerServiceScopeFactory serviceScopeFactory,
        IAtomizerClock clock,
        ILogger logger
    )
    {
        _workerId = workerId;
        _jobProcessorFactory = jobProcessorFactory;
        _serviceScopeFactory = serviceScopeFactory;
        _clock = clock;
        _logger = logger;
    }

    public async Task RunAsync(
        ChannelReader<JobBatch> reader,
        CancellationToken ioToken,
        CancellationToken executionToken
    )
    {
        _logger.LogDebug("Worker {Worker} started", _workerId);

        while (!ioToken.IsCancellationRequested)
        {
            JobBatch batch;
            try
            {
                batch = await reader.ReadAsync(ioToken);
            }
            catch (OperationCanceledException) when (ioToken.IsCancellationRequested)
            {
                _logger.LogDebug("Worker {Worker} cancellation requested", _workerId);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Worker {Worker} channel read operation failed", _workerId);
                _readRetries++;
                if (_readRetries >= MaxReadAttempts)
                {
                    _logger.LogError(
                        "Worker {Worker} exceeded maximum channel read retries, skipping faulty read",
                        _workerId
                    );

                    reader.TryRead(out _); // clear the faulted read
                    _readRetries = 0;
                }
                continue;
            }
            _readRetries = 0; // reset retries on successful read

            var shouldContinue = await ProcessBatchAsync(batch, executionToken);
            if (!shouldContinue)
                break;
        }

        _logger.LogDebug("Worker {Worker} stopped", _workerId);
    }

    private async Task<bool> ProcessBatchAsync(JobBatch batch, CancellationToken executionToken)
    {
        for (var index = 0; index < batch.Jobs.Count; index++)
        {
            var job = batch.Jobs[index];
            var processor = _jobProcessorFactory.Create(_workerId, job.Id);
            var stopPartitionBatch = false;

            try
            {
                await processor.ProcessAsync(job, executionToken);
            }
            catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
            {
                _logger.LogDebug("Worker {Worker} cancellation requested", _workerId);
                await ReleaseRemainingPartitionJobsAsync(batch, index + 1);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker {Worker} failed to process job {JobId}", _workerId, job.Id);
                stopPartitionBatch = batch.IsPartitioned;
            }

            if (executionToken.IsCancellationRequested)
            {
                await ReleaseRemainingPartitionJobsAsync(batch, index + 1);
                return false;
            }

            if (batch.IsPartitioned && (stopPartitionBatch || job.IsPartitionBlocked))
            {
                await ReleaseRemainingPartitionJobsAsync(batch, index + 1);
                return true;
            }
        }

        return true;
    }

    private async Task ReleaseRemainingPartitionJobsAsync(JobBatch batch, int startIndex)
    {
        if (!batch.IsPartitioned || startIndex >= batch.Jobs.Count)
            return;

        var now = _clock.UtcNow;
        var releasedJobs = new List<AtomizerJob>();

        for (var i = startIndex; i < batch.Jobs.Count; i++)
        {
            var job = batch.Jobs[i];
            if (job.Status != AtomizerJobStatus.Processing)
                continue;

            job.Release(now);
            releasedJobs.Add(job);
        }

        if (releasedJobs.Count == 0)
            return;

        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            await scope.Storage.UpdateJobsAsync(releasedJobs, CancellationToken.None);

            _logger.LogDebug(
                "Worker {Worker} released {Count} unprocessed job(s) for partition {PartitionKey}",
                _workerId,
                releasedJobs.Count,
                batch.FirstJob.PartitionKey
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Worker {Worker} failed to release unprocessed job(s) for partition {PartitionKey}",
                _workerId,
                batch.FirstJob.PartitionKey
            );
        }
    }
}
