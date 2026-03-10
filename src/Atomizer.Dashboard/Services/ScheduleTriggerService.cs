using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Dashboard.Services;

/// <summary>
/// Provides on-demand triggering of recurring schedules from the Atomizer Dashboard.
/// Enqueues a new job for the specified schedule immediately and updates the schedule's
/// <see cref="AtomizerSchedule.LastEnqueueAt"/> metadata so the dashboard reflects the
/// manual trigger without waiting for the next polling cycle.
/// </summary>
/// <remarks>
/// The underlying cron cadence (i.e. <see cref="AtomizerSchedule.NextRunAt"/>) is
/// intentionally left unchanged so that the normal scheduler continues on its original
/// cadence after a manual trigger.
/// </remarks>
public sealed class ScheduleTriggerService
{
    private readonly IAtomizerDashboardStorage _storage;
    private readonly IAtomizerClock _clock;
    private readonly ILogger<ScheduleTriggerService> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="ScheduleTriggerService"/>.
    /// </summary>
    /// <param name="storage">
    /// The dashboard storage implementation used to look up the schedule, insert the
    /// new job, and persist updated <see cref="AtomizerSchedule.LastEnqueueAt"/> metadata.
    /// </param>
    /// <param name="clock">Clock used to obtain the current UTC instant.</param>
    /// <param name="logger">Logger for diagnostic and audit output.</param>
    public ScheduleTriggerService(
        IAtomizerDashboardStorage storage,
        IAtomizerClock clock,
        ILogger<ScheduleTriggerService> logger
    )
    {
        _storage = storage;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Fires the specified recurring schedule on-demand by creating and inserting a new
    /// <see cref="AtomizerJob"/> with <c>ScheduledAt = now</c>, then persisting the
    /// updated <see cref="AtomizerSchedule.LastEnqueueAt"/> back to storage so the
    /// dashboard reflects the trigger immediately on the next refresh.
    /// </summary>
    /// <param name="scheduleId">
    /// The unique identifier of the <see cref="AtomizerSchedule"/> to fire on-demand.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The unique identifier of the newly enqueued job.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no schedule with <paramref name="scheduleId"/> exists in storage, or
    /// when the schedule's <see cref="AtomizerSchedule.PayloadType"/> is <see langword="null"/>
    /// and the job cannot be constructed.
    /// </exception>
    public async Task<Guid> TriggerAsync(Guid scheduleId, CancellationToken cancellationToken)
    {
        // 1. Locate the schedule by its identifier.
        var schedules = await _storage.GetAllSchedulesAsync(cancellationToken);
        var schedule = schedules.FirstOrDefault(s => s.Id == scheduleId);

        if (schedule is null)
        {
            throw new InvalidOperationException($"Schedule with id '{scheduleId}' was not found in storage.");
        }

        if (schedule.PayloadType is null)
        {
            throw new InvalidOperationException(
                $"Schedule '{schedule.JobKey.Key}' has no resolved PayloadType "
                    + "and cannot be triggered manually. Ensure the payload assembly is loaded."
            );
        }

        var now = _clock.UtcNow;

        // 2. Build the job.  A "manual" idempotency key scoped to the current instant
        //    ensures that rapid successive triggers each produce a distinct job while
        //    still participating in the idempotency check on high-throughput insertions.
        var idempotencyKey = $"{schedule.JobKey.Key}:manual:{now:O}";

        var job = AtomizerJob.Create(
            schedule.QueueKey,
            schedule.PayloadType,
            schedule.Payload,
            now,
            now, // scheduledAt = now → process immediately
            schedule.RetryStrategy,
            idempotencyKey,
            schedule.JobKey
        );

        var jobId = await _storage.InsertAsync(job, cancellationToken);

        _logger.LogInformation(
            "Manually triggered schedule '{JobKey}' (schedule {ScheduleId}) — enqueued job {JobId}",
            schedule.JobKey.Key,
            scheduleId,
            jobId
        );

        // 3. Update LastEnqueueAt so the dashboard "Last Enqueued" column reflects the
        //    manual trigger without waiting for the next scheduler polling cycle.
        //    NextRunAt is left unchanged so the cron cadence continues unaffected.
        schedule.LastEnqueueAt = now;
        schedule.UpdatedAt = now;

        await _storage.UpdateSchedulesAsync(new[] { schedule }, cancellationToken);

        return jobId;
    }
}
