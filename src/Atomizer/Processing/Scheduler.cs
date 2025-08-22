using System;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Hosting;
using Microsoft.Extensions.Logging;

namespace Atomizer.Processing
{
    internal interface IScheduler
    {
        void Start(CancellationToken cancellationToken);
        Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken);
    }

    internal sealed class Scheduler : IScheduler
    {
        private readonly SchedulingOptions _options;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
        private readonly ILogger<Scheduler> _logger;
        private readonly LeaseToken _leaseToken;

        private Task _processingTask = Task.CompletedTask;
        private DateTimeOffset _lastStorageCheck;

        private CancellationTokenSource _ioCts = new CancellationTokenSource();
        private CancellationTokenSource _executionCts = new CancellationTokenSource();

        private static readonly TimeSpan DefaultTickInterval = TimeSpan.FromSeconds(1);

        public Scheduler(
            AtomizerOptions options,
            IAtomizerClock clock,
            IAtomizerStorageScopeFactory storageScopeFactory,
            ILogger<Scheduler> logger,
            AtomizerRuntimeIdentity identity
        )
        {
            _clock = clock;
            _storageScopeFactory = storageScopeFactory;
            _logger = logger;
            _options = options.SchedulingOptions;
            _leaseToken = new LeaseToken($"{identity.InstanceId}:*:{QueueKey.Scheduler}:*:{Guid.NewGuid():N}");
            _lastStorageCheck = _clock.MinValue;
        }

        public void Start(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting Atomizer Scheduler");
            _ioCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _executionCts = new CancellationTokenSource();
            _processingTask = Task.Run(
                async () => await ProcessDueSchedulesAsync(_ioCts.Token, _executionCts.Token),
                cancellationToken
            );
        }

        public async Task StopAsync(TimeSpan gracePeriod, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Stopping Atomizer Scheduler");

            _ioCts.Cancel();

            try
            {
                await Task.WhenAny(_processingTask, Task.Delay(gracePeriod, cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogDebug("Scheduler stop was cancelled");
            }

            try
            {
                _executionCts.Cancel();
            }
            catch
            {
                _logger.LogDebug("Error cancelling execution token for scheduler");
            }

            try
            {
                await _processingTask.ConfigureAwait(false);
            }
            catch
            {
                _logger.LogDebug("Scheduler processing task completed with errors");
            }

            try
            {
                using var scope = _storageScopeFactory.CreateScope();

                var releaseCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                releaseCts.CancelAfter(TimeSpan.FromSeconds(5));

                var released = await scope.Storage.ReleaseLeasedSchedulesAsync(_leaseToken, releaseCts.Token);
                _logger.LogDebug("Released {Count} schedules with lease token {LeaseToken}", released, _leaseToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release schedules with lease token {LeaseToken}", _leaseToken);
            }
            finally
            {
                _ioCts.Dispose();
                _executionCts.Dispose();
                _logger.LogInformation("Atomizer Scheduler stopped");
            }
        }

        private async Task ProcessDueSchedulesAsync(CancellationToken ioToken, CancellationToken execToken)
        {
            while (!ioToken.IsCancellationRequested)
            {
                try
                {
                    var now = _clock.UtcNow;

                    if (now - _lastStorageCheck >= _options.StorageCheckInterval)
                    {
                        _lastStorageCheck = now;
                        var horizon = now + _options.ScheduleLeadTime!.Value;

                        using var scope = _storageScopeFactory.CreateScope();
                        var storage = scope.Storage;

                        var dueSchedules = await storage.LeaseDueSchedulesAsync(
                            horizon,
                            _options.VisibilityTimeout,
                            _leaseToken,
                            ioToken
                        );

                        foreach (var schedule in dueSchedules)
                        {
                            await ProcessScheduleAsync(schedule, horizon, execToken);
                        }
                    }
                }
                catch (OperationCanceledException) when (ioToken.IsCancellationRequested)
                {
                    _logger.LogDebug("Scheduler processing task was cancelled");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while processing the schedule");
                }

                await Task.Delay(DefaultTickInterval, ioToken);
            }
        }

        private async Task ProcessScheduleAsync(
            AtomizerSchedule schedule,
            DateTimeOffset horizon,
            CancellationToken execToken
        )
        {
            var now = _clock.UtcNow;

            using var scope = _storageScopeFactory.CreateScope();
            var storage = scope.Storage;

            if (schedule.PayloadType is null)
            {
                _logger.LogWarning(
                    "Schedule {ScheduleKey} has no payload type defined, disabling schedule",
                    schedule.JobKey
                );
                schedule.Disable(now);

                try
                {
                    await storage.UpsertScheduleAsync(schedule, execToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to update schedule {ScheduleKey}", schedule.JobKey);
                }

                return;
            }

            var occurrences = schedule.GetOccurrences(horizon);
            foreach (var occurrence in occurrences)
            {
                var idempotencyKey = $"{schedule.JobKey}:*:{occurrence:O}";

                var job = AtomizerJob.Create(
                    schedule.QueueKey,
                    schedule.PayloadType,
                    schedule.Payload,
                    now,
                    occurrence,
                    schedule.MaxAttempts,
                    idempotencyKey,
                    schedule.JobKey
                );

                try
                {
                    await storage.InsertAsync(job, execToken);
                    _logger.LogInformation(
                        "Scheduled job {JobId} for queue '{Queue}' at {VisibleAt}",
                        job.Id,
                        schedule.QueueKey,
                        occurrence
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to insert scheduled job {JobKey}-{JobId} for queue '{Queue}'",
                        job.ScheduleJobKey,
                        job.Id,
                        schedule.QueueKey
                    );
                }
            }

            schedule.UpdateNextOccurence(horizon);
            schedule.Release(now);

            try
            {
                await storage.UpsertScheduleAsync(schedule, execToken);
                _logger.LogDebug(
                    "Updated schedule {ScheduleKey} to next occurrence at {NextOccurrence}",
                    schedule.JobKey,
                    schedule.NextRunAt
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update schedule {ScheduleKey}", schedule.JobKey);
            }
        }
    }
}
