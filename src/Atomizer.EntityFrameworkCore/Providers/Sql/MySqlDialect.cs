using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

internal sealed class MySqlDialect(EntityMap jobs, EntityMap schedules) : BaseSqlDialect(jobs, schedules)
{
    public override FormattableString GetDueJobs(QueueKey queueKey, DateTimeOffset now, int batchSize)
    {
        var format = $$"""
            WITH blocked_partitions AS (
              SELECT DISTINCT {{_jPartitionKey}}
              FROM {{_jTable}}
              WHERE {{_jQueueKey}} = {0}
                AND {{_jPartitionKey}} IS NOT NULL
                AND (
                  {{_jStatus}} = {{_statusProcessing}}
                  OR ({{_jStatus}} = {{_statusPending}} AND {{_jAttempts}} > 0)
                )
            )
            SELECT t.*
            FROM {{_jTable}} AS t
            LEFT JOIN blocked_partitions bp
              ON t.{{_jPartitionKey}} = bp.{{_jPartitionKey}}
            WHERE t.{{_jQueueKey}} = {1}
              AND (t.{{_jPartitionKey}} IS NULL OR bp.{{_jPartitionKey}} IS NULL)
              AND (
                (t.{{_jStatus}} = {{_statusPending}}
                  AND (t.{{_jVisibleAt}} IS NULL OR t.{{_jVisibleAt}} <= {2})
                  AND t.{{_jScheduledAt}} <= {3})
                OR (t.{{_jStatus}} = {{_statusProcessing}} AND t.{{_jVisibleAt}} <= {4})
              )
            ORDER BY t.{{_jScheduledAt}}, t.{{_jPartitionKey}}, t.{{_jSequenceNumber}}, t.{{_jId}}
            LIMIT {5}
            FOR UPDATE SKIP LOCKED;
            """;
        return FormattableStringFactory.Create(
            format,
            queueKey.Key, // {0} blocked_partitions queue filter
            queueKey.Key, // {1} outer SELECT queue filter
            now, // {2} VisibleAt
            now, // {3} ScheduledAt
            now, // {4} Processing VisibleAt
            batchSize // {5} LIMIT
        );
    }

    public override FormattableString InsertJobWithSequence(AtomizerJob job)
    {
        var entity = job.ToEntity();
        var retryIntervals = SerializeIntervals(entity.RetryIntervals);
        var format = $$"""
            INSERT INTO {{_jTable}} (
                {{_jId}}, {{_jQueueKey}}, {{_jPayloadType}}, {{_jPayload}},
                {{_jScheduledAt}}, {{_jVisibleAt}}, {{_jStatus}}, {{_jAttempts}},
                {{_jRetryIntervals}}, {{_jCreatedAt}}, {{_jUpdatedAt}},
                {{_jLeaseToken}}, {{_jScheduleJobKey}}, {{_jIdempotencyKey}},
                {{_jPartitionKey}}, {{_jSequenceNumber}}
            )
            SELECT {0}, {1}, {2}, {3},
                   {4}, {5}, {6}, {7},
                   {8}, {9}, {10},
                   {11}, {12}, {13},
                   {14},
                   COALESCE((SELECT MAX(max_seq) FROM (SELECT MAX({{_jSequenceNumber}}) AS max_seq FROM {{_jTable}} WHERE {{_jQueueKey}} = {15} AND {{_jPartitionKey}} = {16} FOR UPDATE) AS sub), 0) + 1;
            """;
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

    public override FormattableString GetDueSchedules(DateTimeOffset now)
    {
        var format = $$"""
            SELECT t.*
            FROM {{_sTable}} AS t
            WHERE {{_sNextRunAt}} <= {0}
              AND {{_sEnabled}} = TRUE
            ORDER BY {{_sNextRunAt}}, {{_sId}}
            FOR UPDATE SKIP LOCKED;
            """;
        return FormattableStringFactory.Create(format, now);
    }

    public override FormattableString UpsertSchedule(AtomizerSchedule schedule, DateTimeOffset now)
    {
        var entity = schedule.ToEntity();
        var retryIntervals = SerializeIntervals(entity.RetryIntervals);
        var format = $$"""
            INSERT INTO {{_sTable}} (
                {{_sId}},
                {{_sJobKey}},
                {{_sQueueKey}},
                {{_sPayloadType}},
                {{_sPayload}},
                {{_sSchedule}},
                {{_sTimeZone}},
                {{_sMisfirePolicy}},
                {{_sMaxCatchUp}},
                {{_sEnabled}},
                {{_sPartitionKey}},
                {{_sRetryIntervals}},
                {{_sNextRunAt}},
                {{_sLastEnqueueAt}},
                {{_sCreatedAt}},
                {{_sUpdatedAt}}
            ) VALUES (
                {0},
                {1},
                {2},
                {3},
                {4},
                {5},
                {6},
                {7},
                {8},
                {9},
                {10},
                {11},
                {12},
                {13},
                {14},
                {15}
            )
            ON DUPLICATE KEY UPDATE
                {{_sQueueKey}} = VALUES({{_sQueueKey}}),
                {{_sPayloadType}} = VALUES({{_sPayloadType}}),
                {{_sPayload}} = VALUES({{_sPayload}}),
                {{_sSchedule}} = VALUES({{_sSchedule}}),
                {{_sTimeZone}} = VALUES({{_sTimeZone}}),
                {{_sMisfirePolicy}} = VALUES({{_sMisfirePolicy}}),
                {{_sMaxCatchUp}} = VALUES({{_sMaxCatchUp}}),
                {{_sEnabled}} = VALUES({{_sEnabled}}),
                {{_sPartitionKey}} = VALUES({{_sPartitionKey}}),
                {{_sRetryIntervals}} = VALUES({{_sRetryIntervals}}),
                {{_sNextRunAt}} = VALUES({{_sNextRunAt}}),
                {{_sUpdatedAt}} = {15};
            """;
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
            entity.PartitionKey,
            retryIntervals,
            entity.NextRunAt,
            entity.LastEnqueueAt,
            entity.CreatedAt,
            now
        );
    }
}
