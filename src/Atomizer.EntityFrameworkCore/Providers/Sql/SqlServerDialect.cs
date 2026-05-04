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
        var colPartitionKey = c[nameof(AtomizerJobEntity.PartitionKey)];
        var colSequenceNumber = c[nameof(AtomizerJobEntity.SequenceNumber)];
        var colAttempts = c[nameof(AtomizerJobEntity.Attempts)];
        var statusPending = (int)AtomizerEntityJobStatus.Pending;
        var statusProcessing = (int)AtomizerEntityJobStatus.Processing;
        var format =
            $@"WITH blocked_partitions AS (
  SELECT DISTINCT {colPartitionKey}
  FROM {table}
  WHERE {colQueueKey} = {{0}}
    AND {colPartitionKey} IS NOT NULL
    AND (
      {colStatus} = {statusProcessing}
      OR ({colStatus} = {statusPending} AND {colAttempts} > 0)
    )
),
partition_heads AS (
  SELECT {colPartitionKey}, MIN({colSequenceNumber}) AS min_seq
  FROM {table}
  WHERE {colQueueKey} = {{1}}
    AND {colPartitionKey} IS NOT NULL
    AND {colPartitionKey} NOT IN (SELECT {colPartitionKey} FROM blocked_partitions)
    AND (
      ({colStatus} = {statusPending}
        AND ({colVisibleAt} IS NULL OR {colVisibleAt} <= {{9}})
        AND {colScheduledAt} <= {{10}})
      OR ({colStatus} = {statusProcessing} AND {colVisibleAt} <= {{11}})
    )
  GROUP BY {colPartitionKey}
)
SELECT TOP({batchSize}) t.*
FROM {table} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
LEFT JOIN partition_heads ph
  ON t.{colPartitionKey} = ph.{colPartitionKey}
  AND t.{colSequenceNumber} = ph.min_seq
WHERE t.{colQueueKey} = {{2}}
  AND (
    (t.{colPartitionKey} IS NULL
      AND (
        (t.{colStatus} = {statusPending}
          AND (t.{colVisibleAt} IS NULL OR t.{colVisibleAt} <= {{3}})
          AND t.{colScheduledAt} <= {{4}})
        OR (t.{colStatus} = {statusProcessing} AND t.{colVisibleAt} <= {{5}})
      )
    )
    OR
    (t.{colPartitionKey} IS NOT NULL AND ph.min_seq IS NOT NULL
      AND (
        (t.{colStatus} = {statusPending}
          AND (t.{colVisibleAt} IS NULL OR t.{colVisibleAt} <= {{6}})
          AND t.{colScheduledAt} <= {{7}})
        OR (t.{colStatus} = {statusProcessing} AND t.{colVisibleAt} <= {{8}})
      )
    )
  )
ORDER BY t.{colScheduledAt}, t.{colId};";
        return FormattableStringFactory.Create(
            format,
            queueKey.Key,  // {0} blocked_partitions queue filter
            queueKey.Key,  // {1} partition_heads queue filter
            queueKey.Key,  // {2} outer SELECT queue filter
            now,           // {3} unpartitioned VisibleAt
            now,           // {4} unpartitioned ScheduledAt
            now,           // {5} unpartitioned Processing VisibleAt
            now,           // {6} partitioned VisibleAt
            now,           // {7} partitioned ScheduledAt
            now,           // {8} partitioned Processing VisibleAt
            now,           // {9} partition_heads VisibleAt  (batchSize is TOP({batchSize}) inlined, not a placeholder)
            now,           // {10} partition_heads ScheduledAt
            now            // {11} partition_heads Processing VisibleAt
        );
    }

    public FormattableString InsertJobWithSequence(AtomizerJob job)
    {
        var entity = job.ToEntity();
        var table = _jobs.Table;
        var c = _jobs.Col;
        var colId = c[nameof(AtomizerJobEntity.Id)];
        var colQueueKey = c[nameof(AtomizerJobEntity.QueueKey)];
        var colPayloadType = c[nameof(AtomizerJobEntity.PayloadType)];
        var colPayload = c[nameof(AtomizerJobEntity.Payload)];
        var colScheduledAt = c[nameof(AtomizerJobEntity.ScheduledAt)];
        var colVisibleAt = c[nameof(AtomizerJobEntity.VisibleAt)];
        var colStatus = c[nameof(AtomizerJobEntity.Status)];
        var colAttempts = c[nameof(AtomizerJobEntity.Attempts)];
        var colRetryIntervals = c[nameof(AtomizerJobEntity.RetryIntervals)];
        var colCreatedAt = c[nameof(AtomizerJobEntity.CreatedAt)];
        var colUpdatedAt = c[nameof(AtomizerJobEntity.UpdatedAt)];
        var colLeaseToken = c[nameof(AtomizerJobEntity.LeaseToken)];
        var colScheduleJobKey = c[nameof(AtomizerJobEntity.ScheduleJobKey)];
        var colIdempotencyKey = c[nameof(AtomizerJobEntity.IdempotencyKey)];
        var colPartitionKey = c[nameof(AtomizerJobEntity.PartitionKey)];
        var colSequenceNumber = c[nameof(AtomizerJobEntity.SequenceNumber)];
        var retryIntervals = string.Join(
            ";",
            Array.ConvertAll(entity.RetryIntervals, ts => (long)ts.TotalMilliseconds)
        );
        var format =
            $@"INSERT INTO {table} (
    {colId}, {colQueueKey}, {colPayloadType}, {colPayload},
    {colScheduledAt}, {colVisibleAt}, {colStatus}, {colAttempts},
    {colRetryIntervals}, {colCreatedAt}, {colUpdatedAt},
    {colLeaseToken}, {colScheduleJobKey}, {colIdempotencyKey},
    {colPartitionKey}, {colSequenceNumber}
)
SELECT {{0}}, {{1}}, {{2}}, {{3}},
       {{4}}, {{5}}, {{6}}, {{7}},
       {{8}}, {{9}}, {{10}},
       {{11}}, {{12}}, {{13}},
       {{14}},
       COALESCE((SELECT MAX({colSequenceNumber}) FROM {table} WHERE {colQueueKey} = {{15}} AND {colPartitionKey} = {{16}}), 0) + 1;";
        return FormattableStringFactory.Create(
            format,
            entity.Id,
            entity.QueueKey,
            entity.PayloadType,
            entity.Payload,
            entity.ScheduledAt,
            entity.VisibleAt,
            (int)entity.Status,
            entity.Attempts,
            retryIntervals,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.LeaseToken,
            entity.ScheduleJobKey,
            entity.IdempotencyKey,
            entity.PartitionKey,
            entity.QueueKey,
            entity.PartitionKey
        );
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

    public FormattableString GetDueSchedules(DateTimeOffset now)
    {
        var table = _schedules.Table;
        var c = _schedules.Col;
        var colEnabled = c[nameof(AtomizerScheduleEntity.Enabled)];
        var colNextRunAt = c[nameof(AtomizerScheduleEntity.NextRunAt)];
        var colId = c[nameof(AtomizerScheduleEntity.Id)];
        var format =
            $@"SELECT t.*
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
        var retryIntervals = string.Join(
            ";",
            Array.ConvertAll(entity.RetryIntervals, ts => (long)ts.TotalMilliseconds)
        );
        var format =
            $@"MERGE {table} WITH (HOLDLOCK) AS target
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
            entity.JobKey, // {0}
            entity.QueueKey, // {1}
            entity.PayloadType, // {2}
            entity.Payload, // {3}
            entity.Schedule, // {4}
            entity.TimeZone, // {5}
            (int)entity.MisfirePolicy, // {6}
            entity.MaxCatchUp, // {7}
            entity.Enabled ? 1 : 0, // {8}
            retryIntervals, // {9}
            entity.NextRunAt, // {10}
            now, // {11}
            entity.Id, // {12}
            entity.LastEnqueueAt, // {13}
            entity.CreatedAt // {14}
        );
    }
}
