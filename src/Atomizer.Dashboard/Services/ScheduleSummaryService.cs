using Atomizer.Abstractions;
using Atomizer.Dashboard.Models;
using Microsoft.Extensions.Logging;

namespace Atomizer.Dashboard.Services;

/// <summary>
/// Provides a denormalised summary of all recurring schedules suitable for the
/// Atomizer Dashboard recurring-schedules view. Each call to
/// <see cref="GetScheduleSummariesAsync"/> combines <see cref="AtomizerSchedule"/>
/// data with the outcome of the most recent associated job so that the UI receives
/// a single, pre-projected list of <see cref="ScheduleViewModel"/> objects.
/// </summary>
/// <remarks>
/// Storage reads are performed through <see cref="IAtomizerDashboardStorage"/> so
/// that no data store infrastructure is accessed directly. Register this service
/// as <c>Scoped</c> in the DI container alongside the dashboard services.
/// </remarks>
public sealed class ScheduleSummaryService
{
    private readonly IAtomizerDashboardStorage _storage;
    private readonly ILogger<ScheduleSummaryService> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="ScheduleSummaryService"/>.
    /// </summary>
    /// <param name="storage">
    /// The dashboard storage implementation used to query schedule and job data.
    /// </param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public ScheduleSummaryService(IAtomizerDashboardStorage storage, ILogger<ScheduleSummaryService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Queries all <see cref="AtomizerSchedule"/> records from storage and
    /// projects each one — together with the status of its most recent job — into a
    /// <see cref="ScheduleViewModel"/> list ordered alphabetically by job key.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A read-only list of <see cref="ScheduleViewModel"/> objects, one per registered
    /// schedule, ordered by <see cref="ScheduleViewModel.JobKey"/> ascending.
    /// Returns an empty list when no schedules exist or when the storage call fails.
    /// </returns>
    public async Task<IReadOnlyList<ScheduleViewModel>> GetScheduleSummariesAsync(
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var schedules = await _storage.GetAllSchedulesAsync(cancellationToken);

            if (schedules.Count == 0)
            {
                return Array.Empty<ScheduleViewModel>();
            }

            var summaries = new List<ScheduleViewModel>(schedules.Count);

            foreach (var schedule in schedules)
            {
                AtomizerJob? lastJob = null;

                try
                {
                    lastJob = await _storage.GetLastJobForScheduleAsync(schedule.JobKey, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Failed to retrieve last job for schedule {JobKey}; last run status will be unavailable",
                        schedule.JobKey
                    );
                }

                summaries.Add(ProjectToViewModel(schedule, lastJob));
            }

            return summaries;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve schedule summaries from storage.");
            return Array.Empty<ScheduleViewModel>();
        }
    }

    /// <summary>
    /// Projects a single <see cref="AtomizerSchedule"/> and its optional most-recent
    /// <see cref="AtomizerJob"/> into a <see cref="ScheduleViewModel"/>.
    /// </summary>
    /// <param name="schedule">The schedule record to project.</param>
    /// <param name="lastJob">
    /// The most recently updated job associated with this schedule, or
    /// <see langword="null"/> if no job has been produced yet.
    /// </param>
    /// <returns>A fully populated <see cref="ScheduleViewModel"/>.</returns>
    private static ScheduleViewModel ProjectToViewModel(AtomizerSchedule schedule, AtomizerJob? lastJob) =>
        new ScheduleViewModel
        {
            Id = schedule.Id,
            JobKey = schedule.JobKey.Key,
            QueueKey = schedule.QueueKey.Key,
            CronExpression = schedule.Schedule.ToString(),
            NextRunAt = schedule.NextRunAt,
            LastRunAt = schedule.LastEnqueueAt,
            LastRunStatus = lastJob?.Status,
            Enabled = schedule.Enabled,
            MisfirePolicy = schedule.MisfirePolicy,
            PayloadTypeName = schedule.PayloadType?.FullName,
        };
}
