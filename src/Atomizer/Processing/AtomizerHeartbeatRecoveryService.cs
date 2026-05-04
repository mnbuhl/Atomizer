using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing;

internal sealed class AtomizerHeartbeatRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AtomizerRuntimeIdentity _identity;
    private readonly AtomizerProcessingOptions _options;
    private readonly IAtomizerClock _clock;
    private readonly ILogger<AtomizerHeartbeatRecoveryService> _logger;

    public AtomizerHeartbeatRecoveryService(
        IServiceScopeFactory scopeFactory,
        AtomizerRuntimeIdentity identity,
        AtomizerProcessingOptions options,
        IAtomizerClock clock,
        ILogger<AtomizerHeartbeatRecoveryService> logger
    )
    {
        _scopeFactory = scopeFactory;
        _identity = identity;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();

        _logger.LogInformation(
            "Atomizer heartbeat recovery service starting for instance {InstanceId}",
            _identity.InstanceId
        );

        return Task.WhenAll(RunHeartbeatLoopAsync(stoppingToken), RunSweepLoopAsync(stoppingToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var storage = ResolveStorage(scope.ServiceProvider);
            await storage.RemoveHeartbeatAsync(_identity.InstanceId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to remove Atomizer heartbeat for instance {InstanceId}",
                _identity.InstanceId
            );
        }
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await BeatAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Atomizer heartbeat tick");
            }

            await Task.Delay(_options.HeartbeatInterval, cancellationToken);
        }
    }

    private async Task RunSweepLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RecoverAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Atomizer stale-server recovery tick");
            }

            await Task.Delay(_options.EffectiveStaleSweepInterval, cancellationToken);
        }
    }

    private async Task BeatAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var storage = ResolveStorage(scope.ServiceProvider);
        var now = _clock.UtcNow;

        await storage.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = _identity.InstanceId, LastHeartbeatAt = now },
            cancellationToken
        );
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var storage = ResolveStorage(scope.ServiceProvider);
        var now = _clock.UtcNow;

        var staleBefore = now - _options.StaleServerTimeout;
        var staleServers = await storage.GetStaleServersAsync(staleBefore, cancellationToken);

        foreach (var staleServer in staleServers.Where(server => server.InstanceId != _identity.InstanceId))
        {
            var result = await storage.TryRecoverStaleServerAsync(
                staleServer.InstanceId,
                staleBefore,
                now,
                cancellationToken
            );

            if (result.Recovered)
            {
                _logger.LogWarning(
                    "Recovered stale Atomizer instance {InstanceId}; released {ReleasedJobCount} job(s)",
                    result.InstanceId,
                    result.ReleasedJobCount
                );
            }
        }
    }

    private static IAtomizerStorage ResolveStorage(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<IAtomizerStorage>();
}
