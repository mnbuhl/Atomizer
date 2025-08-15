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
    public class QueuePoller
    {
        private readonly QueueOptions _queue;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
        private readonly ILogger<QueuePoller> _logger;
        private readonly string _leaseToken;
        private readonly ChannelWriter<AtomizerJob> _channelWriter;

        private DateTimeOffset _lastStorageCheck;

        public QueuePoller(
            QueueOptions queue,
            IAtomizerClock clock,
            IAtomizerStorageScopeFactory storageScopeFactory,
            ILogger<QueuePoller> logger,
            string leaseToken,
            ChannelWriter<AtomizerJob> channelWriter
        )
        {
            _queue = queue;
            _clock = clock;
            _storageScopeFactory = storageScopeFactory;
            _logger = logger;
            _leaseToken = leaseToken;
            _channelWriter = channelWriter;

            _lastStorageCheck = _clock.MinValue;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var tick = _queue.TickInterval;
            var storageCheckInterval = _queue.StorageCheckInterval;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var now = _clock.UtcNow;
                    if (now - _lastStorageCheck >= storageCheckInterval)
                    {
                        using var scope = _storageScopeFactory.CreateScope();
                        var storage = scope.Storage;

                        _lastStorageCheck = now;

                        var leased = await storage.TryLeaseBatchAsync(
                            _queue.QueueKey,
                            _queue.BatchSize,
                            now,
                            _queue.VisibilityTimeout,
                            _leaseToken,
                            ct
                        );

                        if (leased.Count > 0)
                        {
                            _logger.LogDebug("Queue '{Queue}' leased {Count} job(s)", _queue.QueueKey, leased.Count);

                            foreach (var job in leased)
                            {
                                await _channelWriter.WriteAsync(job, ct);
                            }
                        }
                        else
                        {
                            _logger.LogDebug("Queue '{Queue}' found no jobs to lease", _queue.QueueKey);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Cancellation requested, exit the loop
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in poll loop for queue '{QueueKey}'", _queue.QueueKey);
                }

                try
                {
                    await Task.Delay(tick, ct);
                }
                catch (TaskCanceledException)
                {
                    // Cancellation requested, exit the loop
                    break;
                }
            }
        }
    }
}
