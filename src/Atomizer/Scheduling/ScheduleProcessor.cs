using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Scheduling;

internal interface IScheduleProcessor
{
    Task ProcessAsync(AtomizerSchedule schedule, DateTimeOffset horizon, CancellationToken execToken);
}

internal sealed class ScheduleProcessor : IScheduleProcessor
{
    private readonly IAtomizerClock _clock;
    private readonly IAtomizerStorageScopeFactory _storageScopeFactory;
    private readonly ILogger<ScheduleProcessor> _logger;

    public ScheduleProcessor(
        IAtomizerClock clock,
        IAtomizerStorageScopeFactory storageScopeFactory,
        ILogger<ScheduleProcessor> logger
    )
    {
        _clock = clock;
        _storageScopeFactory = storageScopeFactory;
        _logger = logger;
    }

    public async Task ProcessAsync(AtomizerSchedule schedule, DateTimeOffset horizon, CancellationToken execToken)
    {
        var now = _clock.UtcNow;
        var occurrences = schedule.GetOccurrences(horizon);

        using var scope = _storageScopeFactory.CreateScope();
        var storage = scope.Storage;

        foreach (var occurrence in occurrences)
        {
            var idempotencyKey = $"{schedule.JobKey}:*:{occurrence:O}";

            var job = AtomizerJob.Create(
                schedule.QueueKey,
                schedule.PayloadType!,
                schedule.Payload,
                now,
                occurrence,
                schedule.RetryStrategy,
                idempotencyKey,
                schedule.JobKey
            );

            try
            {
                await storage.InsertAsync(job, execToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to insert scheduled job {JobKey}-{JobId} for queue '{Queue}'",
                    job.ScheduleJobKey,
                    job.Id,
                    job.QueueKey
                );
            }
        }
    }
}
