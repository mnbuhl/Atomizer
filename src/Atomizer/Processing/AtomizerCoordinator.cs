using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal sealed class AtomizerCoordinator
    {
        private readonly AtomizerOptions _options;
        private readonly ILogger<AtomizerCoordinator> _logger;
        private readonly IServiceProvider _serviceProvider;

        private readonly Scheduler _scheduler;
        private readonly List<QueuePump> _queuePumps = new List<QueuePump>();

        public AtomizerCoordinator(
            AtomizerOptions options,
            ILogger<AtomizerCoordinator> logger,
            IServiceProvider serviceProvider
        )
        {
            _options = options;
            _logger = logger;
            _serviceProvider = serviceProvider;

            _scheduler = new Scheduler(_options.SchedulingOptions, _serviceProvider);
        }

        public Task StartAsync(CancellationToken ct)
        {
            _scheduler.Start(ct);

            _logger.LogInformation("Starting {Count} queue pump(s)...", _options.Queues.Count);
            foreach (var queue in _options.Queues)
            {
                var pump = new QueuePump(queue, _serviceProvider);
                _queuePumps.Add(pump);
                pump.Start(ct);
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync(TimeSpan gracePeriod, CancellationToken ct)
        {
            var stopTasks = new List<Task>();
            stopTasks.AddRange(_queuePumps.ConvertAll(p => p.StopAsync(gracePeriod, ct)));
            stopTasks.Add(_scheduler.StopAsync(gracePeriod, ct));
            await Task.WhenAll(stopTasks);
            _logger.LogInformation("Scheduler and all queue pumps stopped");
        }
    }
}
