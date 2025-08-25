using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Scheduling;

internal interface ISchedulePoller
{
    Task RunAsync(CancellationToken ioToken, CancellationToken execToken);
    Task ReleaseLeasedSchedulesAsync(CancellationToken cancellationToken);
}

internal sealed class SchedulePoller : ISchedulePoller
{
    private readonly SchedulingOptions _options;
    private readonly IAtomizerClock _clock;
    private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
    private readonly ILogger<SchedulePoller> _logger;
    private readonly IScheduleProcessor _scheduleProcessor;
    private readonly LeaseToken _leaseToken;

    private DateTimeOffset _lastStorageCheck;

    public SchedulePoller(
        AtomizerOptions options,
        IAtomizerClock clock,
        IAtomizerStorageScopeFactory storageScopeFactory,
        ILogger<SchedulePoller> logger,
        AtomizerRuntimeIdentity identity,
        IScheduleProcessor scheduleProcessor
    )
    {
        _clock = clock;
        _storageScopeFactory = storageScopeFactory;
        _logger = logger;
        _scheduleProcessor = scheduleProcessor;
        _options = options.SchedulingOptions;
        _leaseToken = new LeaseToken($"{identity.InstanceId}:*:{QueueKey.Scheduler}:*:{Guid.NewGuid():N}");
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
                        await _scheduleProcessor.ProcessAsync(schedule, horizon, execToken);
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

    public async Task ReleaseLeasedSchedulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _storageScopeFactory.CreateScope();
            var released = await scope.Storage.ReleaseLeasedSchedulesAsync(_leaseToken, cancellationToken);
            _logger.LogDebug("Released {Count} schedule(s) with lease token {LeaseToken}", released, _leaseToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release schedules with lease token {LeaseToken}", _leaseToken);
        }
    }
}
