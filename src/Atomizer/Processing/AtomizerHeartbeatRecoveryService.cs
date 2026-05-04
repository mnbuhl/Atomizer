using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Exceptions;
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _options.Validate();

        using (var scope = _scopeFactory.CreateScope())
        {
            ResolveRecoveryStorage(scope.ServiceProvider).ValidateHeartbeatRecoverySupport();
        }

        _logger.LogInformation("Atomizer heartbeat recovery service starting for instance {InstanceId}", _identity.InstanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BeatAndRecoverAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Atomizer heartbeat recovery tick");
            }

            await Task.Delay(_options.EffectiveStaleSweepInterval, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var storage = ResolveRecoveryStorage(scope.ServiceProvider);
            await storage.RemoveHeartbeatAsync(_identity.InstanceId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to remove Atomizer heartbeat for instance {InstanceId}", _identity.InstanceId);
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task BeatAndRecoverAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var storage = ResolveRecoveryStorage(scope.ServiceProvider);
        var now = _clock.UtcNow;

        await storage.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = _identity.InstanceId, LastHeartbeatAt = now },
            cancellationToken
        );

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

    private static IAtomizerHeartbeatRecoveryStorage ResolveRecoveryStorage(IServiceProvider serviceProvider)
    {
        var storage = serviceProvider.GetRequiredService<IAtomizerStorage>();
        return storage as IAtomizerHeartbeatRecoveryStorage
            ?? throw new InvalidAtomizerConfigurationException(
                $"The configured Atomizer storage backend must implement {nameof(IAtomizerHeartbeatRecoveryStorage)} to use processing heartbeat recovery."
            );
    }
}
