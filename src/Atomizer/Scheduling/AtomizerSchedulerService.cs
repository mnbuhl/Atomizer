using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atomizer.Scheduling
{
    internal sealed class AtomizerSchedulerService : BackgroundService
    {
        private readonly IScheduler _scheduler;
        private readonly AtomizerProcessingOptions _options;
        private readonly ILogger<AtomizerSchedulerService> _logger;

        public AtomizerSchedulerService(
            IScheduler scheduler,
            AtomizerProcessingOptions options,
            ILogger<AtomizerSchedulerService> logger
        )
        {
            _scheduler = scheduler;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.StartupDelay != null)
            {
                await Task.Delay(_options.StartupDelay.Value, stoppingToken);
            }

            _logger.LogInformation("Atomizer scheduler service starting");
            _scheduler.Start(stoppingToken);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Atomizer scheduler service stopping");
            await _scheduler.StopAsync(_options.GracefulShutdownTimeout, cancellationToken);
            await base.StopAsync(cancellationToken);
            _logger.LogInformation("Atomizer scheduler service stopped");
        }
    }
}
