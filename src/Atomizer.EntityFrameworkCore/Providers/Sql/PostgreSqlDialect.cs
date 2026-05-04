using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

internal sealed class PostgreSqlDialect : ISqlDialect
{
    private readonly EntityMap _jobs;
    private readonly EntityMap _schedules;
    private readonly EntityMap _activeServers;

    public PostgreSqlDialect(EntityMap jobs, EntityMap schedules, EntityMap activeServers)
    {
        _jobs = jobs;
        _schedules = schedules;
        _activeServers = activeServers;
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
        var format =
            $@"SELECT t.*
FROM {table} AS t
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
ORDER BY {colScheduledAt}, {colId}
LIMIT {{4}}
FOR NO KEY UPDATE SKIP LOCKED;";
        return FormattableStringFactory.Create(format, queueKey.Key, now, now, now, batchSize);
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
        var format =
            $@"UPDATE {table}
SET {colStatus} = {statusPending},
    {colLeaseToken} = NULL,
    {colVisibleAt} = NULL,
    {colUpdatedAt} = {{0}}
WHERE {colLeaseToken} = {{1}}
  AND {colStatus} = {statusProcessing};";
        return FormattableStringFactory.Create(format, now, leaseToken.Token);
    }



    public FormattableString DeleteStaleServer(string instanceId, DateTimeOffset staleBefore)
    {
        var table = _activeServers.Table;
        var c = _activeServers.Col;
        var colInstanceId = c[nameof(AtomizerActiveServerEntity.InstanceId)];
        var colLastHeartbeatAt = c[nameof(AtomizerActiveServerEntity.LastHeartbeatAt)];
        var format =
            $@"DELETE FROM {table}
WHERE {colInstanceId} = {{0}}
  AND {colLastHeartbeatAt} < {{1}};";
        return FormattableStringFactory.Create(format, instanceId, staleBefore);
    }

    public FormattableString ReleaseLeasedJobsByInstanceId(string instanceId, DateTimeOffset now)
    {
        var table = _jobs.Table;
        var c = _jobs.Col;
        var colStatus = c[nameof(AtomizerJobEntity.Status)];
        var colLeaseToken = c[nameof(AtomizerJobEntity.LeaseToken)];
        var colVisibleAt = c[nameof(AtomizerJobEntity.VisibleAt)];
        var colUpdatedAt = c[nameof(AtomizerJobEntity.UpdatedAt)];
        var statusPending = (int)AtomizerEntityJobStatus.Pending;
        var statusProcessing = (int)AtomizerEntityJobStatus.Processing;
        var escapedPrefix = EscapeLikePattern(instanceId + LeaseToken.Delimiter) + "%";
        var format =
            $@"UPDATE {table}
SET {colStatus} = {statusPending},
    {colLeaseToken} = NULL,
    {colVisibleAt} = NULL,
    {colUpdatedAt} = {{0}}
WHERE {colLeaseToken} LIKE {{1}} ESCAPE '!'
  AND {colStatus} = {statusProcessing};";
        return FormattableStringFactory.Create(format, now, escapedPrefix);
    }

    public FormattableString GetDueSchedules(DateTimeOffset now)
    {
        var table = _schedules.Table;
        var c = _schedules.Col;
        var colEnabled = c[nameof(AtomizerScheduleEntity.Enabled)];
        var colNextRunAt = c[nameof(AtomizerScheduleEntity.NextRunAt)];
        var colId = c[nameof(AtomizerScheduleEntity.Id)];
        var format =
            $@"SELECT t.*
FROM {table} AS t
WHERE {colEnabled} = TRUE
  AND {colNextRunAt} <= {{0}}
ORDER BY {colNextRunAt}, {colId}
FOR NO KEY UPDATE SKIP LOCKED;";
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
        var retryIntervals = string.Join(
            ";",
            Array.ConvertAll(entity.RetryIntervals, ts => (long)ts.TotalMilliseconds)
        );
        var format =
            $@"INSERT INTO {table} (
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
    {{11}},
    {{12}},
    {{13}},
    {{14}}
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
    {colUpdatedAt} = EXCLUDED.{colUpdatedAt};";
        return FormattableStringFactory.Create(
            format,
            entity.Id,
            entity.JobKey,
            entity.QueueKey,
            entity.PayloadType,
            entity.Payload,
            entity.Schedule,
            entity.TimeZone,
            (int)entity.MisfirePolicy,
            entity.MaxCatchUp,
            entity.Enabled,
            retryIntervals,
            entity.NextRunAt,
            entity.LastEnqueueAt,
            entity.CreatedAt,
            now
        );
    }
    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("!", "!!")
            .Replace("%", "!%")
            .Replace("_", "!_")
            .Replace("[", "![");
    }
}
