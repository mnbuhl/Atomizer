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
        private readonly LeaseToken _leaseToken;
        private readonly Channel<AtomizerJob> _channel;

        private DateTimeOffset _lastStorageCheck;

        private static readonly TimeSpan DefaultTickInterval = TimeSpan.FromSeconds(1);

        public QueuePoller(
            QueueOptions queue,
            IAtomizerClock clock,
            IAtomizerStorageScopeFactory storageScopeFactory,
            ILogger<QueuePoller> logger,
            LeaseToken leaseToken,
            Channel<AtomizerJob> channel
        )
        {
            _queue = queue;
            _clock = clock;
            _storageScopeFactory = storageScopeFactory;
            _logger = logger;
            _leaseToken = leaseToken;
            _channel = channel;

            _lastStorageCheck = _clock.MinValue;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var storageCheckInterval = _queue.StorageCheckInterval;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var now = _clock.UtcNow;
                    var itemsInChannel = _channel.Reader.CanCount ? _channel.Reader.Count : 0;
                    if (now - _lastStorageCheck >= storageCheckInterval && itemsInChannel < _queue.DegreeOfParallelism)
                    {
                        using var scope = _storageScopeFactory.CreateScope();
                        var storage = scope.Storage;

                        _lastStorageCheck = now;

                        var leased = await storage.LeaseBatchAsync(
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
                                await _channel.Writer.WriteAsync(job, ct);
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
                    await Task.Delay(DefaultTickInterval, ct);
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
