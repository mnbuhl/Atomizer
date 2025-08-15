using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Hosting;
using Atomizer.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    public class QueuePump
    {
        private readonly QueueOptions _queue;
        private readonly DefaultRetryPolicy _retryPolicy;
        private readonly IAtomizerJobDispatcher _dispatcher;
        private readonly IAtomizerClock _clock;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<QueuePump> _logger;
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory;

        private readonly Channel<AtomizerJob> _channel;
        private readonly List<Task> _workers = new List<Task>();
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly QueuePoller _poller;

        private readonly string _leaseToken;

        public QueuePump(QueueOptions queue, DefaultRetryPolicy retryPolicy, IServiceProvider serviceProvider)
        {
            _queue = queue;
            _retryPolicy = retryPolicy;
            _dispatcher = serviceProvider.GetRequiredService<IAtomizerJobDispatcher>();
            _clock = serviceProvider.GetRequiredService<IAtomizerClock>();
            _loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            _logger = _loggerFactory.CreateLogger<QueuePump>();

            _channel = ChannelFactory.CreateBounded<AtomizerJob>(
                capacity: Math.Max(1, _queue.DegreeOfParallelism) * Math.Max(1, _queue.BatchSize)
            );

            var identity = serviceProvider.GetRequiredService<AtomizerRuntimeIdentity>();
            _leaseToken = $"{identity.InstanceId}-{_queue.QueueKey}:{Guid.NewGuid():N}";

            _storageScopeFactory = new ServiceProviderStorageScopeFactory(serviceProvider);

            _poller = new QueuePoller(
                _queue,
                _clock,
                _storageScopeFactory,
                _loggerFactory.CreateLogger<QueuePoller>(),
                _leaseToken,
                _channel.Writer
            );
        }

        public void Start(CancellationToken cancellationToken)
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _logger.LogInformation(
                "Starting queue '{QueueKey}' with {Workers} workers",
                _queue.QueueKey,
                _queue.DegreeOfParallelism
            );

            // Start poller
            _ = Task.Run(async () => await _poller.RunAsync(_cts.Token), _cts.Token);

            // Start workers
            var workers = Math.Max(1, _queue.DegreeOfParallelism);
            for (int i = 0; i < workers; i++)
            {
                var workerId = $"{_queue.QueueKey}-{i}";
                var worker = new JobWorker(
                    workerId,
                    _queue,
                    _clock,
                    _dispatcher,
                    _retryPolicy,
                    _storageScopeFactory,
                    _loggerFactory,
                    _leaseToken
                );

                var task = Task.Run(async () => await worker.RunAsync(_channel.Reader, _cts.Token), _cts.Token);
                _workers.Add(task);
            }
        }

        public async Task StopAsync()
        {
            _logger.LogInformation("Stopping queue '{QueueKey}'...", _queue.QueueKey);
            _cts.Cancel();
            _channel.Writer.TryComplete();

            try
            {
                await Task.WhenAll(_workers);
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while stopping queue '{QueueKey}'", _queue.QueueKey);
            }
            finally
            {
                _cts.Dispose();
            }

            _logger.LogInformation("Queue '{QueueKey}' stopped", _queue.QueueKey);
        }
    }
}
