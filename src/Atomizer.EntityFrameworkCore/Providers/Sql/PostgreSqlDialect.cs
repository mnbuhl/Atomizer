using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

internal sealed class PostgreSqlDialect : ISqlDialect
{
    private readonly EntityMap _jobs;
    private readonly EntityMap _schedules;

    public PostgreSqlDialect(EntityMap jobs, EntityMap schedules)
    {
        _jobs = jobs;
        _schedules = schedules;
    }

    public FormattableString GetDueJobs(QueueKey queueKey, DateTimeOffset now, int batchSize)
    {
        var table = _jobs.Table;
        var c = _jobs.Col;
        var colStatus = c[nameof(AtomizerJobEntity.Status)];
        var colQueueKey = c[nameof(AtomizerJobEntity.QueueKey)];
        var colVisibleAt = c[nameof(AtomizerJobEntity.VisibleAt)];
        var colScheduledAt = c[nameof(AtomizerJobEntity.ScheduledAt)];
        var colId = c[nameof(AtomizerJobEntity.Id)];
        var statusPending = (int)AtomizerEntityJobStatus.Pending;
        var statusProcessing = (int)AtomizerEntityJobStatus.Processing;
        return $"""
            SELECT t.*
            FROM {table} AS t
            WHERE {colQueueKey} = {queueKey.Key}
              AND (
                    ( {colStatus} = {statusPending}
                      AND ( {colVisibleAt} IS NULL
                            OR {colVisibleAt} <= {now})
                      AND {colScheduledAt} <= {now}
                    )
                    OR
                    ( {colStatus} = {statusProcessing}
                      AND {colVisibleAt} <= {now}
                    )
                  )
            ORDER BY {colScheduledAt}, {colId}
            LIMIT {batchSize}
            FOR NO KEY UPDATE SKIP LOCKED;
            """;
    }

    public FormattableString ReleaseLeasedJobs(LeaseToken leaseToken, DateTimeOffset now)
    {
        var table = _jobs.Table;
        var c = _jobs.Col;
        var colStatus = c[nameof(AtomizerJobEntity.Status)];
        var colLeaseToken = c[nameof(AtomizerJobEntity.LeaseToken)];
        var colVisibleAt = c[nameof(AtomizerJobEntity.VisibleAt)];
        var colUpdatedAt = c[nameof(AtomizerJobEntity.UpdatedAt)];
        var statusPending = (int)AtomizerEntityJobStatus.Pending;
        var statusProcessing = (int)AtomizerEntityJobStatus.Processing;
        return $"""
            UPDATE {table}
            SET {colStatus} = {statusPending},
                {colLeaseToken} = NULL,
                {colVisibleAt} = NULL,
                {colUpdatedAt} = {now}
            WHERE {colLeaseToken} = {leaseToken.Token}
              AND {colStatus} = {statusProcessing};
            """;
    }

    public FormattableString GetDueSchedules(DateTimeOffset now)
    {
        var table = _schedules.Table;
        var c = _schedules.Col;
        var colEnabled = c[nameof(AtomizerScheduleEntity.Enabled)];
        var colNextRunAt = c[nameof(AtomizerScheduleEntity.NextRunAt)];
        var colId = c[nameof(AtomizerScheduleEntity.Id)];
        return $"""
            SELECT t.*
            FROM {table} AS t
            WHERE {colEnabled} = TRUE
              AND {colNextRunAt} <= {now}
            ORDER BY {colNextRunAt}, {colId}
            FOR NO KEY UPDATE SKIP LOCKED;
            """;
    }

    public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule, DateTimeOffset now)
    {
        var entity = schedule.ToEntity();
        var table = _schedules.Table;
        var c = _schedules.Col;
        var colId = c[nameof(AtomizerScheduleEntity.Id)];
        var colJobKey = c[nameof(AtomizerScheduleEntity.JobKey)];
        var colQueueKey = c[nameof(AtomizerScheduleEntity.QueueKey)];
        var colPayloadType = c[nameof(AtomizerScheduleEntity.PayloadType)];
        var colPayload = c[nameof(AtomizerScheduleEntity.Payload)];
        var colSchedule = c[nameof(AtomizerScheduleEntity.Schedule)];
        var colTimeZone = c[nameof(AtomizerScheduleEntity.TimeZone)];
        var colMisfirePolicy = c[nameof(AtomizerScheduleEntity.MisfirePolicy)];
        var colMaxCatchUp = c[nameof(AtomizerScheduleEntity.MaxCatchUp)];
        var colEnabled = c[nameof(AtomizerScheduleEntity.Enabled)];
        var colRetryIntervals = c[nameof(AtomizerScheduleEntity.RetryIntervals)];
        var colNextRunAt = c[nameof(AtomizerScheduleEntity.NextRunAt)];
        var colLastEnqueueAt = c[nameof(AtomizerScheduleEntity.LastEnqueueAt)];
        var colCreatedAt = c[nameof(AtomizerScheduleEntity.CreatedAt)];
        var colUpdatedAt = c[nameof(AtomizerScheduleEntity.UpdatedAt)];
        var retryIntervals = string.Join(";", Array.ConvertAll(entity.RetryIntervals, ts => (long)ts.TotalMilliseconds));
        return $"""
            INSERT INTO {table} (
                {colId},
                {colJobKey},
                {colQueueKey},
                {colPayloadType},
                {colPayload},
                {colSchedule},
                {colTimeZone},
                {colMisfirePolicy},
                {colMaxCatchUp},
                {colEnabled},
                {colRetryIntervals},
                {colNextRunAt},
                {colLastEnqueueAt},
                {colCreatedAt},
                {colUpdatedAt}
            ) VALUES (
                {entity.Id},
                {entity.JobKey},
                {entity.QueueKey},
                {entity.PayloadType},
                {entity.Payload},
                {entity.Schedule},
                {entity.TimeZone},
                {(int)entity.MisfirePolicy},
                {entity.MaxCatchUp},
                {entity.Enabled},
                {retryIntervals},
                {entity.NextRunAt},
                {entity.LastEnqueueAt},
                {entity.CreatedAt},
                {now}
            )
            ON CONFLICT ({colJobKey}) DO UPDATE SET
                {colQueueKey} = EXCLUDED.{colQueueKey},
                {colPayloadType} = EXCLUDED.{colPayloadType},
                {colPayload} = EXCLUDED.{colPayload},
                {colSchedule} = EXCLUDED.{colSchedule},
                {colTimeZone} = EXCLUDED.{colTimeZone},
                {colMisfirePolicy} = EXCLUDED.{colMisfirePolicy},
                {colMaxCatchUp} = EXCLUDED.{colMaxCatchUp},
                {colEnabled} = EXCLUDED.{colEnabled},
                {colRetryIntervals} = EXCLUDED.{colRetryIntervals},
                {colNextRunAt} = EXCLUDED.{colNextRunAt},
                {colUpdatedAt} = EXCLUDED.{colUpdatedAt};
            """;
    }
}
