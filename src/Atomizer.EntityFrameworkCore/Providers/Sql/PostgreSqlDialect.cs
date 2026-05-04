using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

internal sealed class PostgreSqlDialect(EntityMap jobs, EntityMap schedules) : BaseSqlDialect(jobs, schedules)
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
            ),
            partition_heads AS (
              SELECT {{_jPartitionKey}}, MIN({{_jSequenceNumber}}) AS min_seq
              FROM {{_jTable}}
              WHERE {{_jQueueKey}} = {1}
                AND {{_jPartitionKey}} IS NOT NULL
                AND {{_jPartitionKey}} NOT IN (SELECT {{_jPartitionKey}} FROM blocked_partitions)
                AND (
                  ({{_jStatus}} = {{_statusPending}}
                    AND ({{_jVisibleAt}} IS NULL OR {{_jVisibleAt}} <= {10})
                    AND {{_jScheduledAt}} <= {11})
                  OR ({{_jStatus}} = {{_statusProcessing}} AND {{_jVisibleAt}} <= {12})
                )
              GROUP BY {{_jPartitionKey}}
            )
            SELECT t.*
            FROM {{_jTable}} AS t
            LEFT JOIN partition_heads ph
              ON t.{{_jPartitionKey}} = ph.{{_jPartitionKey}}
              AND t.{{_jSequenceNumber}} = ph.min_seq
            WHERE t.{{_jQueueKey}} = {2}
              AND (
                (t.{{_jPartitionKey}} IS NULL
                  AND (
                    (t.{{_jStatus}} = {{_statusPending}}
                      AND (t.{{_jVisibleAt}} IS NULL OR t.{{_jVisibleAt}} <= {3})
                      AND t.{{_jScheduledAt}} <= {4})
                    OR (t.{{_jStatus}} = {{_statusProcessing}} AND t.{{_jVisibleAt}} <= {5})
                  )
                )
                OR
                (t.{{_jPartitionKey}} IS NOT NULL AND ph.min_seq IS NOT NULL
                  AND (
                    (t.{{_jStatus}} = {{_statusPending}}
                      AND (t.{{_jVisibleAt}} IS NULL OR t.{{_jVisibleAt}} <= {6})
                      AND t.{{_jScheduledAt}} <= {7})
                    OR (t.{{_jStatus}} = {{_statusProcessing}} AND t.{{_jVisibleAt}} <= {8})
                  )
                )
              )
            ORDER BY t.{{_jScheduledAt}}, t.{{_jId}}
            LIMIT {9}
            FOR NO KEY UPDATE SKIP LOCKED;
            """;
        return FormattableStringFactory.Create(
            format,
            queueKey.Key, // {0} blocked_partitions queue filter
            queueKey.Key, // {1} partition_heads queue filter
            queueKey.Key, // {2} outer SELECT queue filter
            now, // {3} unpartitioned VisibleAt
            now, // {4} unpartitioned ScheduledAt
            now, // {5} unpartitioned Processing VisibleAt
            now, // {6} partitioned VisibleAt
            now, // {7} partitioned ScheduledAt
            now, // {8} partitioned Processing VisibleAt
            batchSize, // {9} LIMIT
            now, // {10} partition_heads VisibleAt
            now, // {11} partition_heads ScheduledAt
            now // {12} partition_heads Processing VisibleAt
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
                   COALESCE((SELECT MAX({{_jSequenceNumber}}) FROM (SELECT {{_jSequenceNumber}} FROM {{_jTable}} WHERE {{_jQueueKey}} = {15} AND {{_jPartitionKey}} = {16}) AS sub), 0) + 1;
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
            WHERE {{_sEnabled}} = TRUE
              AND {{_sNextRunAt}} <= {0}
            ORDER BY {{_sNextRunAt}}, {{_sId}}
            FOR NO KEY UPDATE SKIP LOCKED;
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
                {14}
            )
            ON CONFLICT ({{_sJobKey}}) DO UPDATE SET
                {{_sQueueKey}} = EXCLUDED.{{_sQueueKey}},
                {{_sPayloadType}} = EXCLUDED.{{_sPayloadType}},
                {{_sPayload}} = EXCLUDED.{{_sPayload}},
                {{_sSchedule}} = EXCLUDED.{{_sSchedule}},
                {{_sTimeZone}} = EXCLUDED.{{_sTimeZone}},
                {{_sMisfirePolicy}} = EXCLUDED.{{_sMisfirePolicy}},
                {{_sMaxCatchUp}} = EXCLUDED.{{_sMaxCatchUp}},
                {{_sEnabled}} = EXCLUDED.{{_sEnabled}},
                {{_sRetryIntervals}} = EXCLUDED.{{_sRetryIntervals}},
                {{_sNextRunAt}} = EXCLUDED.{{_sNextRunAt}},
                {{_sUpdatedAt}} = EXCLUDED.{{_sUpdatedAt}};
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
            retryIntervals,
            entity.NextRunAt,
            entity.LastEnqueueAt,
            entity.CreatedAt,
            now
        );
    }
}
