using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal sealed class AtomizerQueueService : BackgroundService
    {
        private readonly IQueueCoordinator _queueCoordinator;
        private readonly AtomizerProcessingOptions _options;
        private readonly ILogger<AtomizerQueueService> _logger;

        public AtomizerQueueService(
            IQueueCoordinator queueCoordinator,
            AtomizerProcessingOptions options,
            ILogger<AtomizerQueueService> logger
        )
        {
            _queueCoordinator = queueCoordinator;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.StartupDelay != null)
            {
                await Task.Delay(_options.StartupDelay.Value, stoppingToken);
            }

            _logger.LogInformation("Atomizer queue service starting");
            _queueCoordinator.Start(stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Atomizer queue service stopping");
            await _queueCoordinator.StopAsync(_options.GracefulShutdownTimeout, cancellationToken);
            await base.StopAsync(cancellationToken);
            _logger.LogInformation("Atomizer queue service stopped");
        }
    }
}
