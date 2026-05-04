using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

internal sealed class SqlServerDialect(EntityMap jobs, EntityMap schedules) : BaseSqlDialect(jobs, schedules)
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
                    AND ({{_jVisibleAt}} IS NULL OR {{_jVisibleAt}} <= {9})
                    AND {{_jScheduledAt}} <= {10})
                  OR ({{_jStatus}} = {{_statusProcessing}} AND {{_jVisibleAt}} <= {11})
                )
              GROUP BY {{_jPartitionKey}}
            )
            SELECT TOP({{batchSize}}) t.*
            FROM {{_jTable}} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
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
            ORDER BY t.{{_jScheduledAt}}, t.{{_jId}};
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
            now, // {9} partition_heads VisibleAt  (batchSize is TOP(batchSize) inlined, not a placeholder)
            now, // {10} partition_heads ScheduledAt
            now // {11} partition_heads Processing VisibleAt
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
                   COALESCE((SELECT MAX({{_jSequenceNumber}}) FROM {{_jTable}} WITH (UPDLOCK, HOLDLOCK) WHERE {{_jQueueKey}} = {15} AND {{_jPartitionKey}} = {16}), 0) + 1;
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
            FROM {{_sTable}} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
            WHERE {{_sNextRunAt}} <= {0}
              AND {{_sEnabled}} = 1
            ORDER BY {{_sNextRunAt}}, {{_sId}};
            """;
        return FormattableStringFactory.Create(format, now);
    }

    public override FormattableString UpsertSchedule(AtomizerSchedule schedule, DateTimeOffset now)
    {
        var entity = schedule.ToEntity();
        var retryIntervals = SerializeIntervals(entity.RetryIntervals);
        var format = $$"""
            MERGE {{_sTable}} WITH (HOLDLOCK) AS target
            USING (SELECT {0}) AS src ({{_sJobKey}})
            ON target.{{_sJobKey}} = src.{{_sJobKey}}
            WHEN MATCHED THEN UPDATE SET
                {{_sQueueKey}} = {1},
                {{_sPayloadType}} = {2},
                {{_sPayload}} = {3},
                {{_sSchedule}} = {4},
                {{_sTimeZone}} = {5},
                {{_sMisfirePolicy}} = {6},
                {{_sMaxCatchUp}} = {7},
                {{_sEnabled}} = {8},
                {{_sRetryIntervals}} = {9},
                {{_sNextRunAt}} = {10},
                {{_sUpdatedAt}} = {11}
            WHEN NOT MATCHED THEN INSERT (
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
                {12},
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
                {13},
                {14},
                {11}
            );
            """;
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
