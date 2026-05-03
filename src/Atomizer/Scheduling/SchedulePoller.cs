using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Scheduling;

internal interface ISchedulePoller
{
    Task RunAsync(CancellationToken ioToken, CancellationToken execToken);
}

internal sealed class SchedulePoller : ISchedulePoller
{
    private readonly SchedulingOptions _options;
    private readonly IAtomizerClock _clock;
    private readonly IAtomizerServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<SchedulePoller> _logger;
    private readonly IScheduleProcessor _scheduleProcessor;

    private DateTimeOffset _lastStorageCheck;

    public SchedulePoller(
        AtomizerOptions options,
        IAtomizerClock clock,
        IAtomizerServiceScopeFactory serviceScopeFactory,
        ILogger<SchedulePoller> logger,
        IScheduleProcessor scheduleProcessor
    )
    {
        _clock = clock;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _scheduleProcessor = scheduleProcessor;
        _options = options.SchedulingOptions;
        _lastStorageCheck = _clock.MinValue;
    }

    public async Task RunAsync(CancellationToken ioToken, CancellationToken execToken)
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

                    using var scope = _serviceScopeFactory.CreateScope();
                    var storage = scope.Storage;

                    await storage.ExecuteInLeaseAsync(
                        QueueKey.Scheduler,
                        async innerCt =>
                        {
<<<<<<< HEAD
                            var dueSchedules = await storage.GetDueSchedulesAsync(horizon, ioToken);
=======
                            var dueSchedules = await storage.GetDueSchedulesAsync(horizon, innerCt);
>>>>>>> worktree-agent-a8c50de87634b18f9

                            foreach (var schedule in dueSchedules)
                            {
                                if (schedule.PayloadType is null)
                                {
                                    _logger.LogWarning(
                                        "Schedule {ScheduleKey} has no payload type defined, disabling schedule",
                                        schedule.JobKey
                                    );
                                    schedule.Disable(now);
                                    continue;
                                }

                                await _scheduleProcessor.ProcessAsync(schedule, horizon, execToken);
                                schedule.UpdateNextOccurence(horizon, now);
                            }

<<<<<<< HEAD
                            await storage.UpdateSchedulesAsync(dueSchedules, execToken);
                        },
                        execToken
=======
                            await storage.UpdateSchedulesAsync(dueSchedules, innerCt);
                        },
                        ioToken
>>>>>>> worktree-agent-a8c50de87634b18f9
                    );
                }
            }
            catch (OperationCanceledException) when (ioToken.IsCancellationRequested)
            {
                _logger.LogDebug("Scheduler poller task was cancelled");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while polling the schedule");
            }

            try
            {
                await Task.Delay(_options.TickInterval, ioToken);
            }
            catch (OperationCanceledException) when (ioToken.IsCancellationRequested)
            {
                // Ignore cancellation during delay
                return;
            }
        }
    }
}
