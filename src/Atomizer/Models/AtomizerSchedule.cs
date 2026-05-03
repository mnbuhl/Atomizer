using Atomizer.Models.Base;
using Cronos;

namespace Atomizer;

/// <summary>
/// Represents a recurring schedule definition in the Atomizer system.
/// </summary>
public class AtomizerSchedule : Model
{
    /// <summary>
    /// Gets or sets the unique key identifying this recurring schedule.
    /// </summary>
    public JobKey JobKey { get; set; } = new JobKey("default");

    /// <summary>
    /// Gets or sets the queue into which occurrences of this schedule are enqueued.
    /// </summary>
    public QueueKey QueueKey { get; set; } = QueueKey.Default;

    /// <summary>
    /// Gets or sets the CLR type of the serialized payload.
    /// </summary>
    public Type? PayloadType { get; set; }

    /// <summary>
    /// Gets or sets the serialized payload string used for each job occurrence.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cron-based schedule defining when occurrences run.
    /// </summary>
    public Schedule Schedule { get; set; } = Schedule.Default;

    /// <summary>
    /// Gets or sets the time zone in which the cron expression is evaluated.
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Gets or sets the policy controlling behavior when a scheduled run is missed.
    /// </summary>
    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.ExecuteNow;

    /// <summary>
    /// Gets or sets the maximum number of missed runs to catch up on when using <see cref="MisfirePolicy.CatchUp"/>.
    /// </summary>
    public int MaxCatchUp { get; set; } = 5; // Default to catching up 5 missed runs

    /// <summary>
    /// Gets or sets whether this schedule is active and will be polled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the retry strategy applied when a job occurrence fails.
    /// </summary>
    public RetryStrategy RetryStrategy { get; set; } = RetryStrategy.Default;

    /// <summary>
    /// Gets or sets the UTC time of the next scheduled occurrence.
    /// </summary>
    public DateTimeOffset NextRunAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time the most recent occurrence was enqueued, or null if never enqueued.
    /// </summary>
    public DateTimeOffset? LastEnqueueAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time this schedule was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time this schedule was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    private CronExpression CronExpression => CronExpression.Parse(Schedule.ToString(), CronFormat.IncludeSeconds);

    /// <summary>
    /// Creates a new <see cref="AtomizerSchedule"/> with the initial next run time computed from the cron expression.
    /// </summary>
    /// <param name="jobKey">The unique key identifying this schedule.</param>
    /// <param name="queueKey">The queue into which occurrences are enqueued.</param>
    /// <param name="payloadType">The CLR type of the serialized payload.</param>
    /// <param name="payload">The serialized payload string for each occurrence.</param>
    /// <param name="schedule">The cron-based schedule expression.</param>
    /// <param name="timeZone">The time zone for cron evaluation.</param>
    /// <param name="createdAt">The UTC time the schedule was created.</param>
    /// <param name="misfirePolicy">Policy for handling missed runs. Defaults to <see cref="MisfirePolicy.ExecuteNow"/>.</param>
    /// <param name="maxCatchUp">Maximum missed runs to catch up. Defaults to 5.</param>
    /// <param name="enabled">Whether the schedule is active. Defaults to true.</param>
    /// <param name="retryStrategy">Optional retry strategy; defaults to <see cref="RetryStrategy.Default"/>.</param>
    /// <returns>A new <see cref="AtomizerSchedule"/> instance.</returns>
    public static AtomizerSchedule Create(
        JobKey jobKey,
        QueueKey queueKey,
        Type payloadType,
        string payload,
        Schedule schedule,
        TimeZoneInfo timeZone,
        DateTimeOffset createdAt,
        MisfirePolicy misfirePolicy = MisfirePolicy.ExecuteNow,
        int maxCatchUp = 5,
        bool enabled = true,
        RetryStrategy? retryStrategy = null
    )
    {
        var atomizerSchedule = new AtomizerSchedule
        {
            Id = Guid.NewGuid(),
            JobKey = jobKey,
            QueueKey = queueKey,
            PayloadType = payloadType,
            Payload = payload,
            Schedule = schedule,
            TimeZone = timeZone,
            MisfirePolicy = misfirePolicy,
            MaxCatchUp = maxCatchUp,
            Enabled = enabled,
            RetryStrategy = retryStrategy ?? RetryStrategy.Default,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

        atomizerSchedule.NextRunAt =
            atomizerSchedule.CronExpression.GetNextOccurrence(createdAt, timeZone) ?? DateTimeOffset.MaxValue;

        return atomizerSchedule;
    }

    /// <summary>
    /// Returns the list of job occurrences that should be enqueued at or before <paramref name="now"/>,
    /// applying the configured <see cref="AtomizerSchedule.MisfirePolicy"/>.
    /// </summary>
    /// <param name="now">The current UTC time.</param>
    /// <returns>An ordered list of UTC occurrence timestamps to enqueue.</returns>
    public List<DateTimeOffset> GetOccurrences(DateTimeOffset now)
    {
        var occurrences = new List<DateTimeOffset>();

        if (NextRunAt > now)
        {
            return occurrences;
        }

        switch (MisfirePolicy)
        {
            case MisfirePolicy.Ignore:
                break;
            case MisfirePolicy.ExecuteNow:
                occurrences.Add(NextRunAt);
                break;
            case MisfirePolicy.CatchUp:
                var from = (LastEnqueueAt ?? CreatedAt).AddTicks(1);
                occurrences.AddRange(
                    CronExpression
                        .GetOccurrences(from, now, TimeZone)
                        .OrderBy(dt => dt)
                        .Take(MaxCatchUp)
                );
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(MisfirePolicy), MisfirePolicy, "Invalid misfire policy");
        }

        return occurrences;
    }

    /// <summary>
    /// Advances <see cref="AtomizerSchedule.NextRunAt"/> to the next cron occurrence after <paramref name="horizon"/>.
    /// </summary>
    /// <param name="horizon">The UTC time from which to compute the next occurrence.</param>
    /// <param name="now">The current UTC time used to set <see cref="AtomizerSchedule.UpdatedAt"/>.</param>
    public void UpdateNextOccurence(DateTimeOffset horizon, DateTimeOffset now)
    {
        var nextOccurrence = CronExpression.GetNextOccurrence(horizon, TimeZone);
        NextRunAt = nextOccurrence ?? DateTimeOffset.MaxValue; // No further occurrences
        LastEnqueueAt = horizon;
        UpdatedAt = now;
    }

    /// <summary>
    /// Disables this schedule so it is no longer polled.
    /// </summary>
    /// <param name="now">The current UTC time used to set <see cref="AtomizerSchedule.UpdatedAt"/>.</param>
    public void Disable(DateTimeOffset now)
    {
        Enabled = false;
        UpdatedAt = now;
    }
}

/// <summary>
/// Controls how the scheduler handles a schedule whose NextRunAt was missed.
/// </summary>
public enum MisfirePolicy
{
    /// <summary>
    /// Skip the missed run and advance to the next scheduled occurrence.
    /// </summary>
    Ignore = 1, // skip this run; advance to next

    /// <summary>
    /// Enqueue one job immediately for the missed occurrence, then advance.
    /// </summary>
    ExecuteNow = 2, // enqueue one now; then advance one step

    /// <summary>
    /// Enqueue one job per missed occurrence, up to <see cref="AtomizerSchedule.MaxCatchUp"/>.
    /// </summary>
    CatchUp = 3, // enqueue all missed (bounded by MaxCatchUp)
}
