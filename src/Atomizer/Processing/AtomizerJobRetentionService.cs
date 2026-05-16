using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal sealed class AtomizerJobRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AtomizerProcessingOptions _options;
    private readonly IAtomizerClock _clock;
    private readonly ILogger<AtomizerJobRetentionService> _logger;

    public AtomizerJobRetentionService(
        IServiceScopeFactory scopeFactory,
        AtomizerProcessingOptions options,
        IAtomizerClock clock,
        ILogger<AtomizerJobRetentionService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();

        if (_options.JobRetention is null)
        {
            return;
        }

        if (_options.StartupDelay != null)
        {
            await Task.Delay(_options.StartupDelay.Value, stoppingToken);
        }

        _logger.LogInformation("Atomizer job retention service starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeleteExpiredJobsAsync(_options.JobRetention.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Atomizer job retention tick");
            }

            await Task.Delay(_options.JobRetentionSweepInterval, stoppingToken);
        }
    }

    private async Task DeleteExpiredJobsAsync(TimeSpan retention, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        var cutoff = _clock.UtcNow - retention;

        var deleted = await storage.DeleteExpiredJobsAsync(cutoff, cancellationToken);
        if (deleted > 0)
        {
            _logger.LogInformation("Deleted {Count} expired Atomizer job(s) older than {Cutoff:o}", deleted, cutoff);
        }
    }
}
