using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal interface IQueuePump
    {
        void Start(CancellationToken cancellationToken);
        Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken);
    }

    internal sealed class QueuePump : IQueuePump
    {
        private readonly QueueOptions _queue;
        private readonly ILogger<QueuePump> _logger;
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
        private readonly IJobWorkerFactory _workerFactory;
        private readonly IQueuePoller _poller;

        private readonly Channel<AtomizerJob> _channel;
        private readonly List<Task> _workers = new List<Task>();
        private Task _pollTask = Task.CompletedTask;

        private CancellationTokenSource _ioCts = new CancellationTokenSource();
        private CancellationTokenSource _executionCts = new CancellationTokenSource();

        private readonly LeaseToken _leaseToken;

        public QueuePump(
            QueueOptions queue,
            IQueuePoller poller,
            IAtomizerStorageScopeFactory storageScopeFactory,
            ILogger<QueuePump> logger,
            IJobWorkerFactory workerFactory,
            AtomizerRuntimeIdentity identity
        )
        {
            _queue = queue;
            _poller = poller;
            _storageScopeFactory = storageScopeFactory;
            _logger = logger;
            _workerFactory = workerFactory;

            _channel = Channel.CreateBounded<AtomizerJob>(
                new BoundedChannelOptions(Math.Max(1, _queue.DegreeOfParallelism) * Math.Max(1, _queue.BatchSize))
                {
                    SingleReader = false,
                    SingleWriter = true,
                    FullMode = BoundedChannelFullMode.Wait,
                }
            );

            _leaseToken = new LeaseToken($"{identity.InstanceId}:*:{_queue.QueueKey}:*:{Guid.NewGuid():N}");
        }

        public void Start(CancellationToken cancellationToken)
        {
            _ioCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executionCts = new CancellationTokenSource();

            _logger.LogInformation(
                "Starting queue '{QueueKey}' with {Workers} workers",
                _queue.QueueKey,
                _queue.DegreeOfParallelism
            );

            // Start poller
            _pollTask = Task.Run(
                async () => await _poller.RunAsync(_queue, _leaseToken, _channel, _ioCts.Token),
                _ioCts.Token
            );

            // Start workers
            var workers = Math.Max(1, _queue.DegreeOfParallelism);
            for (int i = 0; i < workers; i++)
            {
                var workerId = $"{_queue.QueueKey}-{i}";
                var worker = _workerFactory.Create(workerId);

                var task = Task.Run(
                    async () => await worker.RunAsync(_channel.Reader, _ioCts.Token, _executionCts.Token),
                    CancellationToken.None
                );

                _workers.Add(task);
            }
        }

        public async Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping queue '{QueueKey}'...", _queue.QueueKey);

            // 1) Enter draining mode: stop leasing & fetching new jobs immediately
            _ioCts.Cancel(); // Signal poller to stop fetching new jobs & job worker to stop reading from the channel
            _channel.Writer.TryComplete();

            // 2) Wait for all workers to finish processing current jobs until grace period expires
            var workers = _workers.ToArray();
            Task allWorkers = Task.WhenAll(workers);
            Task deadline = Task.Delay(gracePeriod, cancellationToken);

            var finished = await Task.WhenAny(allWorkers, deadline);

            if (finished == deadline)
            {
                _logger.LogWarning(
                    "Graceful shutdown timeout for queue '{QueueKey}' reached, forcing stop",
                    _queue.QueueKey
                );

                // 3) Cancel execution for long-running jobs
                try
                {
                    _executionCts.Cancel();
                }
                catch
                {
                    _logger.LogDebug("Error cancelling execution for queue '{QueueKey}'", _queue.QueueKey);
                }
                try
                {
                    await allWorkers.ConfigureAwait(false);
                }
                catch
                {
                    _logger.LogDebug("Error waiting for workers to finish for queue '{QueueKey}'", _queue.QueueKey);
                }
            }

            // Ensure poller finished
            try
            {
                await _pollTask.ConfigureAwait(false);
            }
            catch
            {
                _logger.LogDebug("Error waiting for poller to finish for queue '{QueueKey}'", _queue.QueueKey);
            }

            // 4) Release any remaining leases for this pump
            try
            {
                using var scope = _storageScopeFactory.CreateScope();
                var releaseCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                releaseCts.CancelAfter(TimeSpan.FromSeconds(5));
                var released = await scope.Storage.ReleaseLeasedAsync(_leaseToken, releaseCts.Token);
                if (released > 0)
                    _logger.LogInformation(
                        "Released {Count} leased job(s) for queue '{QueueKey}'",
                        released,
                        _queue.QueueKey
                    );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error releasing leased jobs for queue '{QueueKey}'. Jobs will reappear after visibility timeout",
                    _queue.QueueKey
                );
            }
            finally
            {
                _ioCts.Dispose();
                _executionCts.Dispose();
            }

            _logger.LogInformation("Queue '{QueueKey}' stopped", _queue.QueueKey);
        }
    }
}
