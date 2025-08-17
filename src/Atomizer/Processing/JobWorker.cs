using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Hosting;
using Atomizer.Models;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal sealed class JobWorker
    {
        private readonly string _workerId;
        private readonly QueueOptions _queue;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerJobDispatcher _dispatcher;
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly LeaseToken _leaseToken;

        public JobWorker(
            string workerId,
            QueueOptions queue,
            IAtomizerClock clock,
            IAtomizerJobDispatcher dispatcher,
            IAtomizerStorageScopeFactory storageScopeFactory,
            ILoggerFactory loggerFactory,
            LeaseToken leaseToken
        )
        {
            _workerId = workerId;
            _queue = queue;
            _clock = clock;
            _dispatcher = dispatcher;
            _storageScopeFactory = storageScopeFactory;
            _loggerFactory = loggerFactory;
            _leaseToken = leaseToken;

            _logger = loggerFactory.CreateLogger($"Worker.{workerId}");
        }

        public async Task RunAsync(
            ChannelReader<AtomizerJob> reader,
            CancellationToken ioToken,
            CancellationToken executionToken
        )
        {
            _logger.LogDebug("Worker {Worker} for '{Queue}' started", _workerId, _queue.QueueKey);

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
                    _logger.LogWarning("Worker {Worker} read operation failed, stopping worker", _workerId);
                    break;
                }

                using var scope = _storageScopeFactory.CreateScope();
                var storage = scope.Storage;

                var jobLogger = _loggerFactory.CreateLogger($"Worker.{_workerId}-{job.Id}");
                var processor = new JobProcessor(_queue, _clock, _dispatcher, storage, jobLogger, _leaseToken);

                try
                {
                    await processor.ProcessAsync(job, executionToken);
                }
                catch (OperationCanceledException) when (executionToken.IsCancellationRequested)
                {
                    jobLogger.LogDebug("Worker {Worker} cancellation requested", _workerId);
                    break;
                }
            }

            _logger.LogDebug("Worker {Worker} for '{Queue}' stopped", _workerId, _queue.QueueKey);
        }
    }
}
