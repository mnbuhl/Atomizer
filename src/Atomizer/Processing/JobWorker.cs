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
        private readonly WorkerId _workerId;
        private readonly IJobProcessorFactory _jobProcessorFactory;
        private readonly ILogger _logger;

        private int _readRetries = 0;
        private const int MaxReadAttempts = 5;

        public JobWorker(WorkerId workerId, IJobProcessorFactory jobProcessorFactory, ILogger logger)
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

                var processor = _jobProcessorFactory.Create(_workerId, job.Id);

                try
                {
                    await processor.ProcessAsync(job, executionToken);
                }
                catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Worker {Worker} cancellation requested", _workerId);
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
