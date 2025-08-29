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
                    var leasingScopeFactory = scope.LeasingScopeFactory;

#if NETCOREAPP3_0_OR_GREATER
                    await using var leasingScope = await leasingScopeFactory.CreateScopeAsync(
                        QueueKey.Scheduler,
                        TimeSpan.FromMinutes(1),
                        execToken
                    );
#else
                    using var leasingScope = await leasingScopeFactory.CreateScopeAsync(
                        QueueKey.Scheduler,
                        TimeSpan.FromMinutes(1),
                        execToken
                    );
#endif
                    if (leasingScope.Acquired)
                    {
                        var storage = scope.Storage;

                        var dueSchedules = await storage.GetDueSchedulesAsync(horizon, ioToken);

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

                        await storage.UpdateSchedulesAsync(dueSchedules, execToken);
                    }
                    else
                    {
                        _logger.LogDebug("Could not acquire leasing scope for schedule poller");
                    }
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
