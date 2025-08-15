using System.Threading;
using System.Threading.Tasks;
using Atomizer.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    public class AtomizerHostedService : BackgroundService
    {
        private readonly IQueueCoordinator _coordinator;
        private readonly AtomizerProcessingOptions _options;
        private readonly ILogger<AtomizerHostedService> _logger;

        public AtomizerHostedService(
            IQueueCoordinator coordinator,
            AtomizerProcessingOptions options,
            ILogger<AtomizerHostedService> logger
        )
        {
            _coordinator = coordinator;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.StartupDelay != null)
            {
                await Task.Delay(_options.StartupDelay!.Value, stoppingToken);
            }

            _logger.LogInformation("Atomizer hosted service starting");
            await _coordinator.StartAsync(stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Atomizer hosted service stopping");
            await _coordinator.StopAsync(_options.GracefulShutdownTimeout, cancellationToken);
            await base.StopAsync(cancellationToken);
            _logger.LogInformation("Atomizer hosted service stopped");
        }
    }
}
