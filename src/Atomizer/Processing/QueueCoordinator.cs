using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Hosting;
using Microsoft.Extensions.Logging;

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
        private readonly IAtomizerJobDispatcher _jobDispatcher;
        private readonly IAtomizerClock _clock;
        private readonly ILogger<QueueCoordinator> _logger;
        private readonly IServiceProvider _serviceProvider;

        private readonly List<QueuePump> _queuePumps = new List<QueuePump>();

        public QueueCoordinator(
            AtomizerOptions options,
            IAtomizerJobDispatcher jobDispatcher,
            IAtomizerClock clock,
            ILogger<QueueCoordinator> logger,
            IServiceProvider serviceProvider
        )
        {
            _options = options;
            _jobDispatcher = jobDispatcher;
            _clock = clock;
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken ct)
        {
            _logger.LogInformation("Starting {Count} queue pump(s)...", _options.Queues.Count);
            foreach (var queue in _options.Queues)
            {
                var pump = new QueuePump(
                    queue,
                    new DefaultRetryPolicy(queue.RetryOptions),
                    _jobDispatcher,
                    _clock,
                    _serviceProvider
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
