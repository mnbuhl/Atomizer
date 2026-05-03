using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

internal sealed class SqlServerDialect : ISqlDialect
{
    private readonly EntityMap _jobs;
    private readonly EntityMap _schedules;

    public SqlServerDialect(EntityMap jobs, EntityMap schedules)
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
            SELECT TOP({batchSize}) t.*
            FROM {table} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
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
            ORDER BY {colScheduledAt}, {colId};
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
            FROM {table} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE {colNextRunAt} <= {now}
              AND {colEnabled} = 1
            ORDER BY {colNextRunAt}, {colId};
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
            MERGE {table} WITH (HOLDLOCK) AS target
            USING (SELECT {entity.JobKey}) AS src ({colJobKey})
            ON target.{colJobKey} = src.{colJobKey}
            WHEN MATCHED THEN UPDATE SET
                {colQueueKey} = {entity.QueueKey},
                {colPayloadType} = {entity.PayloadType},
                {colPayload} = {entity.Payload},
                {colSchedule} = {entity.Schedule},
                {colTimeZone} = {entity.TimeZone},
                {colMisfirePolicy} = {(int)entity.MisfirePolicy},
                {colMaxCatchUp} = {entity.MaxCatchUp},
                {colEnabled} = {(entity.Enabled ? 1 : 0)},
                {colRetryIntervals} = {retryIntervals},
                {colNextRunAt} = {entity.NextRunAt},
                {colUpdatedAt} = {now}
            WHEN NOT MATCHED THEN INSERT (
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
                {(entity.Enabled ? 1 : 0)},
                {retryIntervals},
                {entity.NextRunAt},
                {entity.LastEnqueueAt},
                {entity.CreatedAt},
                {now}
            );
            """;
    }
}
