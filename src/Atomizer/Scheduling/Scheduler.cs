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
        private readonly ILogger<Scheduler> _logger;
        private readonly LeaseToken _leaseToken;
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory;

        private DateTimeOffset _lastStorageCheck;

        private static readonly TimeSpan DefaultTickInterval = TimeSpan.FromSeconds(1);

        public Scheduler(
            AtomizerOptions options,
            IAtomizerClock clock,
            ILogger<Scheduler> logger,
            AtomizerRuntimeIdentity identity,
            IAtomizerStorageScopeFactory storageScopeFactory
        )
        {
            _options = options.SchedulerOptions;
            _clock = clock;
            _logger = logger;
            _storageScopeFactory = storageScopeFactory;

            _lastStorageCheck = _clock.MinValue;

            _leaseToken = new LeaseToken($"{identity.InstanceId}:*:{_options.DefaultQueue}:*:{Guid.NewGuid():N}");
        }

        public async Task StartAsync(CancellationToken ct)
        {
            var storageCheckInterval = _options.StorageCheckInterval;

            while (!ct.IsCancellationRequested)
            {
                var now = _clock.UtcNow;
                if (now - _lastStorageCheck >= storageCheckInterval) { }
            }
        }

        public async Task StopAsync(CancellationToken ct)
        {
            // Logic to stop the scheduler
        }
    }
}
