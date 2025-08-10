using System.Threading;
using System.Threading.Tasks;
using Atomizer.Configuration;
using Microsoft.Extensions.Hosting;

namespace Atomizer.Processing
{
    public class AtomizerHostedService : BackgroundService
    {
        private readonly IQueueCoordinator _coordinator;
        private readonly AtomizerProcessingOptions _options;

        public AtomizerHostedService(IQueueCoordinator coordinator, AtomizerProcessingOptions options)
        {
            _coordinator = coordinator;
            _options = options;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.StartupDelay != null)
            {
                await Task.Delay(_options.StartupDelay!.Value, stoppingToken);
            }
            await _coordinator.StartAsync(stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _coordinator.StopAsync();
            await base.StopAsync(cancellationToken);
        }
    }
}
