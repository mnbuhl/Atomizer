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
        private readonly IAtomizerJobStorage _jobStorage;
        private readonly IAtomizerJobDispatcher _jobDispatcher;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerLogger<QueueCoordinator> _logger;
        private readonly IAtomizerServiceResolver _serviceResolver;

        private readonly List<QueuePump> _queuePumps = new List<QueuePump>();

        public QueueCoordinator(
            AtomizerOptions options,
            IAtomizerJobStorage jobStorage,
            IAtomizerJobDispatcher jobDispatcher,
            IAtomizerClock clock,
            IAtomizerLogger<QueueCoordinator> logger,
            IAtomizerServiceResolver serviceResolver
        )
        {
            _options = options;
            _jobStorage = jobStorage;
            _jobDispatcher = jobDispatcher;
            _clock = clock;
            _logger = logger;
            _serviceResolver = serviceResolver;
        }

        public Task StartAsync(CancellationToken ct)
        {
            _logger.LogInformation("Starting {Count} queue pump(s)...", _options.Queues.Count);
            foreach (var queue in _options.Queues)
            {
                var pumpLogger = _serviceResolver.Resolve<IAtomizerLogger<QueuePump>>();
                var pump = new QueuePump(
                    queue,
                    _options,
                    new DefaultRetryPolicy(queue.RetryOptions),
                    _jobStorage,
                    _jobDispatcher,
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
                await pump.StopAsync();
            }

            _logger.LogInformation("All queue pumps stopped");
        }
    }
}
