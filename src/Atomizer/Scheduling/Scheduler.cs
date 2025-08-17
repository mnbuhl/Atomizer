using System;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Hosting;
using Atomizer.Models;
using Microsoft.Extensions.Logging;

namespace Atomizer.Scheduling
{
    public class Scheduler
    {
        private readonly SchedulerOptions _options;
        private readonly IAtomizerClock _clock;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<Scheduler> _logger;
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
        private readonly LeaseToken _leaseToken;

        private DateTimeOffset _lastStorageCheck;

        private static readonly TimeSpan DefaultTickInterval = TimeSpan.FromSeconds(1);

        public Scheduler(
            AtomizerOptions options,
            IAtomizerClock clock,
            ILoggerFactory loggerFactory,
            AtomizerRuntimeIdentity identity,
            IAtomizerStorageScopeFactory storageScopeFactory
        )
        {
            _options = options.SchedulerOptions;
            _clock = clock;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<Scheduler>();
            _storageScopeFactory = storageScopeFactory;

            _lastStorageCheck = _clock.MinValue;

            _leaseToken = new LeaseToken($"{identity.InstanceId}:*:{_options.DefaultQueue}:*:{Guid.NewGuid():N}");
        }

        public async Task StartAsync(CancellationToken ct)
        {
            var storageCheckInterval = _options.StorageCheckInterval;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var now = _clock.UtcNow;

                    if (now - _lastStorageCheck >= storageCheckInterval)
                    {
                        using var scope = _storageScopeFactory.CreateScope();
                        var storage = scope.Storage;
                        _lastStorageCheck = now;

                        // Poll for jobs that are due to run
                        var leased = await storage.LeaseDueRecurringAsync(
                            _options.DefaultQueue,
                            now + storageCheckInterval, // account for delay
                            _leaseToken,
                            ct
                        );

                        foreach (var recurringJob in leased)
                        {
                            _logger.LogDebug("Queuing scheduled job: {JobName}", recurringJob.Name);
                            await ProcessScheduleAsync(recurringJob, ct);
                        }
                    }

                    await Task.Delay(DefaultTickInterval, ct);
                }
                catch (TaskCanceledException)
                {
                    // Cancellation requested, exit the loop
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in scheduler loop");
                    await Task.Delay(DefaultTickInterval, ct);
                }
            }
        }

        public async Task StopAsync(CancellationToken ct)
        {
            // Logic to stop the scheduler
        }

        private async Task ProcessScheduleAsync(AtomizerRecurringJob recurringJob, CancellationToken ct) { }
    }
}
