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
        private readonly IAtomizerLogger _logger;

        private readonly List<QueuePump> _queuePumps = new List<QueuePump>();

        public QueueCoordinator(
            AtomizerOptions options,
            IJobStorage jobStorage,
            IJobDispatcher jobDispatcher,
            IRetryPolicy retryPolicy,
            IAtomizerClock clock,
            IAtomizerLogger logger
        )
        {
            _options = options;
            _jobStorage = jobStorage;
            _jobDispatcher = jobDispatcher;
            _retryPolicy = retryPolicy;
            _clock = clock;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken ct)
        {
            foreach (var queue in _options.Queues)
            {
                var pump = new QueuePump();
                _queuePumps.Add(pump);
                pump.StartAsync(ct);
            }

            return Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            foreach (var pump in _queuePumps)
            {
                await pump.StopAsync();
            }
        }
    }
}
