using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.Hosting;
using Atomizer.Models;
using Cronos;
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

                        // Schedule job ahead of 1 sweep to account for processing delays
                        var nowPlusInterval = now + storageCheckInterval;

                        // Poll for jobs that are due to run
                        var leased = await storage.LeaseDueRecurringAsync(
                            _options.DefaultQueue,
                            nowPlusInterval, // account for delay
                            _leaseToken,
                            ct
                        );

                        foreach (var recurringJob in leased)
                        {
                            _logger.LogDebug("Queuing scheduled job: {JobName}", recurringJob.Name);
                            await ProcessScheduleAsync(recurringJob, nowPlusInterval, ct);
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

        private async Task ProcessScheduleAsync(
            AtomizerRecurringJob recurringJob,
            DateTimeOffset now,
            CancellationToken ct
        )
        {
            try
            {
                var timezone = recurringJob.TimeZoneId;
                var cron = CronExpression.Parse(recurringJob.CronExpression);

                var nextOccurrence = recurringJob.NextOccurrence;
                var occurrences = new List<DateTimeOffset>();

                if (nextOccurrence <= now)
                {
                    switch (recurringJob.MisfirePolicy)
                    {
                        case AtomizerMisfirePolicy.Ignore:
                            nextOccurrence = NextAfter(cron, now, timezone);
                            break;
                        case AtomizerMisfirePolicy.RunNow:
                            occurrences.Add(now);
                            nextOccurrence = NextAfter(cron, now, timezone);
                            break;
                        case AtomizerMisfirePolicy.CatchUp:
                            occurrences.AddRange(
                                cron.GetOccurrences(
                                    recurringJob.LastOccurrence ?? recurringJob.CreatedAt,
                                    now,
                                    timezone
                                )
                            );
                            nextOccurrence = NextAfter(cron, occurrences.Max(), timezone);

                            break;
                        default:
                            throw new ArgumentOutOfRangeException(
                                nameof(recurringJob.MisfirePolicy),
                                recurringJob.MisfirePolicy,
                                "Unknown misfire policy"
                            );
                    }
                }

                using var scope = _storageScopeFactory.CreateScope();
                var storage = scope.Storage;

                foreach (var occurrence in occurrences)
                {
                    var job = AtomizerJob.Create(
                        recurringJob.QueueKey,
                        recurringJob.PayloadType,
                        recurringJob.Payload,
                        occurrence,
                        recurringJob.MaxAttempts,
                        recurringJob.Id
                    );

                    await storage.InsertAsync(job, ct);
                }

                recurringJob.NextOccurrence = nextOccurrence;
                recurringJob.LastOccurrence = now;
                recurringJob.LeaseToken = null;

                await storage.UpdateRecurringAsync(recurringJob, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing scheduled job: {JobName}", recurringJob.Name);
            }
        }

        private static DateTimeOffset NextAfter(CronExpression cron, DateTimeOffset after, TimeZoneInfo timezone) =>
            cron.GetNextOccurrence(after, timezone) ?? DateTimeOffset.MaxValue;
    }
}
