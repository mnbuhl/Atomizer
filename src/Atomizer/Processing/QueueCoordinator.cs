using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Hosting;

namespace Atomizer.Processing
{
    public interface IQueueCoordinator
    {
        Task StartAsync(CancellationToken ct);
        Task StopAsync();
    }

    public class QueueCoordinator : IQueueCoordinator
    {
        private readonly AtomizerOptions _options;
        private readonly IJobStorage _jobStorage;
        private readonly IJobDispatcher _jobDispatcher;
        private readonly IRetryPolicy _retryPolicy;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerLogger<QueueCoordinator> _logger;
        private readonly IAtomizerServiceResolver _serviceResolver;

        private readonly List<QueuePump> _queuePumps = new List<QueuePump>();

        public QueueCoordinator(
            AtomizerOptions options,
            IJobStorage jobStorage,
            IJobDispatcher jobDispatcher,
            IRetryPolicy retryPolicy,
            IAtomizerClock clock,
            IAtomizerLogger<QueueCoordinator> logger,
            IAtomizerServiceResolver serviceResolver
        )
        {
            _options = options;
            _jobStorage = jobStorage;
            _jobDispatcher = jobDispatcher;
            _retryPolicy = retryPolicy;
            _clock = clock;
            _logger = logger;
            _serviceResolver = serviceResolver;
        }

        public Task StartAsync(CancellationToken ct)
        {
            foreach (var queue in _options.Queues)
            {
                _logger.LogInformation("Starting queue pump for queue: {QueueKey}", queue.QueueKey);
                var pumpLogger = _serviceResolver.Resolve<IAtomizerLogger<QueuePump>>();
                var pump = new QueuePump(
                    queue,
                    _options,
                    _jobStorage,
                    _jobDispatcher,
                    _retryPolicy,
                    _clock,
                    pumpLogger
                );
                _queuePumps.Add(pump);
                pump.Start(ct);
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            foreach (var pump in _queuePumps)
            {
                _logger.LogInformation("Stopping queue pump for queue: {QueueKey}", pump.QueueKey);
                await pump.StopAsync();
            }
        }
    }
}
