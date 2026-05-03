using System.Runtime.CompilerServices;
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
        var format = $@"SELECT TOP({batchSize}) t.*
FROM {table} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
WHERE {colQueueKey} = {{0}}
  AND (
        ( {colStatus} = {statusPending}
          AND ( {colVisibleAt} IS NULL
                OR {colVisibleAt} <= {{1}})
          AND {colScheduledAt} <= {{2}}
        )
        OR
        ( {colStatus} = {statusProcessing}
          AND {colVisibleAt} <= {{3}}
        )
      )
ORDER BY {colScheduledAt}, {colId};";
        return FormattableStringFactory.Create(format, queueKey.Key, now, now, now);
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
        var format = $@"UPDATE {table}
SET {colStatus} = {statusPending},
    {colLeaseToken} = NULL,
    {colVisibleAt} = NULL,
    {colUpdatedAt} = {{0}}
WHERE {colLeaseToken} = {{1}}
  AND {colStatus} = {statusProcessing};";
        return FormattableStringFactory.Create(format, now, leaseToken.Token);
    }

    public FormattableString GetDueSchedules(DateTimeOffset now)
    {
        var table = _schedules.Table;
        var c = _schedules.Col;
        var colEnabled = c[nameof(AtomizerScheduleEntity.Enabled)];
        var colNextRunAt = c[nameof(AtomizerScheduleEntity.NextRunAt)];
        var colId = c[nameof(AtomizerScheduleEntity.Id)];
        var format = $@"SELECT t.*
FROM {table} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
WHERE {colNextRunAt} <= {{0}}
  AND {colEnabled} = 1
ORDER BY {colNextRunAt}, {colId};";
        return FormattableStringFactory.Create(format, now);
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
        var format = $@"MERGE {table} WITH (HOLDLOCK) AS target
USING (SELECT {{0}}) AS src ({colJobKey})
ON target.{colJobKey} = src.{colJobKey}
WHEN MATCHED THEN UPDATE SET
    {colQueueKey} = {{1}},
    {colPayloadType} = {{2}},
    {colPayload} = {{3}},
    {colSchedule} = {{4}},
    {colTimeZone} = {{5}},
    {colMisfirePolicy} = {{6}},
    {colMaxCatchUp} = {{7}},
    {colEnabled} = {{8}},
    {colRetryIntervals} = {{9}},
    {colNextRunAt} = {{10}},
    {colUpdatedAt} = {{11}}
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
    {{12}},
    {{0}},
    {{1}},
    {{2}},
    {{3}},
    {{4}},
    {{5}},
    {{6}},
    {{7}},
    {{8}},
    {{9}},
    {{10}},
    {{13}},
    {{14}},
    {{11}}
);";
        return FormattableStringFactory.Create(
            format,
            entity.JobKey,              // {0}
            entity.QueueKey,            // {1}
            entity.PayloadType,         // {2}
            entity.Payload,             // {3}
            entity.Schedule,            // {4}
            entity.TimeZone,            // {5}
            (int)entity.MisfirePolicy,  // {6}
            entity.MaxCatchUp,          // {7}
            entity.Enabled ? 1 : 0,     // {8}
            retryIntervals,             // {9}
            entity.NextRunAt,           // {10}
            now,                        // {11}
            entity.Id,                  // {12}
            entity.LastEnqueueAt,       // {13}
            entity.CreatedAt            // {14}
        );
    }
}
