using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal interface IJobWorker
    {
        WorkerId WorkerId { get; }
        Task RunAsync(ChannelReader<AtomizerJob> reader, CancellationToken ioToken, CancellationToken executionToken);
    }

    internal sealed class JobWorker : IJobWorker
    {
        public WorkerId WorkerId { get; }
        private readonly IJobProcessorFactory _jobProcessorFactory;
        private readonly ILogger _logger;

        public JobWorker(WorkerId workerId, IJobProcessorFactory jobProcessorFactory, ILogger logger)
        {
            WorkerId = workerId;
            _jobProcessorFactory = jobProcessorFactory;
            _logger = logger;
        }

        public async Task RunAsync(
            ChannelReader<AtomizerJob> reader,
            CancellationToken ioToken,
            CancellationToken executionToken
        )
        {
            _logger.LogDebug("Worker {Worker} started", WorkerId);

            while (!ioToken.IsCancellationRequested)
            {
                AtomizerJob job;
                try
                {
                    job = await reader.ReadAsync(ioToken);
                }
                catch (OperationCanceledException) when (ioToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Worker {Worker} cancellation requested", WorkerId);
                    break;
                }
                catch
                {
                    _logger.LogWarning("Worker {Worker} read operation failed", WorkerId);
                    continue;
                }

                var processorId = $"{WorkerId}-{job.Id}";
                var processor = _jobProcessorFactory.Create(processorId);

                try
                {
                    await processor.ProcessAsync(job, executionToken);
                }
                catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Worker {Worker} cancellation requested", WorkerId);
                    break;
                }
                catch (TaskCanceledException)
                {
                    _logger.LogDebug("Worker {Worker} task was canceled", WorkerId);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker {Worker} failed to process job {JobId}", WorkerId, job.Id);
                    // Optionally handle job failure, e.g., requeue or log
                }
            }

            _logger.LogDebug("Worker {Worker} stopped", WorkerId);
        }
    }
}
