using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Atomizer.Processing
{
    public class AtomizerHostedService : BackgroundService
    {
        private readonly IQueueCoordinator _coordinator;

        public AtomizerHostedService(IQueueCoordinator coordinator)
        {
            _coordinator = coordinator;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return _coordinator.StartAsync(stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _coordinator.StopAsync();
            await base.StopAsync(cancellationToken);
        }
    }
}
