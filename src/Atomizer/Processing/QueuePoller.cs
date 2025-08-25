using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal interface IQueuePoller
    {
        Task RunAsync(QueueOptions queue, LeaseToken leaseToken, Channel<AtomizerJob> channel, CancellationToken ct);
    }

    internal class QueuePoller : IQueuePoller
    {
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
        private readonly ILogger<QueuePoller> _logger;

        private DateTimeOffset _lastStorageCheck;

        public QueuePoller(
            IAtomizerClock clock,
            IAtomizerStorageScopeFactory storageScopeFactory,
            ILogger<QueuePoller> logger
        )
        {
            _clock = clock;
            _storageScopeFactory = storageScopeFactory;
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
                try
                {
                    var now = _clock.UtcNow;
                    var itemsInChannel = channel.Reader.CanCount ? channel.Reader.Count : 0;
                    if (now - _lastStorageCheck >= storageCheckInterval && itemsInChannel < queue.DegreeOfParallelism)
                    {
                        using var scope = _storageScopeFactory.CreateScope();
                        var storage = scope.Storage;

                        _lastStorageCheck = now;

                        var leased = await storage.LeaseBatchAsync(
                            queue.QueueKey,
                            queue.BatchSize,
                            now,
                            queue.VisibilityTimeout,
                            leaseToken,
                            ct
                        );

                        if (leased.Count > 0)
                        {
                            _logger.LogDebug("Queue '{Queue}' leased {Count} job(s)", queue.QueueKey, leased.Count);

                            foreach (var job in leased)
                            {
                                await channel.Writer.WriteAsync(job, ct);
                            }
                        }
                        else
                        {
                            _logger.LogDebug("Queue '{Queue}' found no jobs to lease", queue.QueueKey);
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
        }
    }
}
