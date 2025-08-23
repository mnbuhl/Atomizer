using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal interface IJobWorker
    {
        Task RunAsync(ChannelReader<AtomizerJob> reader, CancellationToken ioToken, CancellationToken executionToken);
    }

    internal sealed class JobWorker : IJobWorker
    {
        private readonly string _workerId;
        private readonly IJobProcessorFactory _jobProcessorFactory;
        private readonly ILogger _logger;

        public JobWorker(string workerId, IJobProcessorFactory jobProcessorFactory, ILogger logger)
        {
            _workerId = workerId;
            _jobProcessorFactory = jobProcessorFactory;
            _logger = logger;
        }

        public async Task RunAsync(
            ChannelReader<AtomizerJob> reader,
            CancellationToken ioToken,
            CancellationToken executionToken
        )
        {
            _logger.LogDebug("Worker {Worker} started", _workerId);

            while (!ioToken.IsCancellationRequested)
            {
                AtomizerJob job;
                try
                {
                    job = await reader.ReadAsync(ioToken);
                }
                catch (OperationCanceledException) when (ioToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Worker {Worker} cancellation requested", _workerId);
                    break;
                }
                catch
                {
                    _logger.LogWarning("Worker {Worker} read operation failed", _workerId);
                    continue;
                }

                var processorId = $"{_workerId}-{job.Id}";
                var processor = _jobProcessorFactory.Create(processorId);

                try
                {
                    await processor.ProcessAsync(job, executionToken);
                }
                catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Worker {Worker} cancellation requested", _workerId);
                    break;
                }
                catch (TaskCanceledException)
                {
                    _logger.LogDebug("Worker {Worker} task was canceled", _workerId);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker {Worker} failed to process job {JobId}", _workerId, job.Id);
                    // Optionally handle job failure, e.g., requeue or log
                }
            }

            _logger.LogDebug("Worker {Worker} stopped", _workerId);
        }
    }
}
