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
            )
            SELECT TOP({{batchSize}}) t.*
            FROM {{_jTable}} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
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
            ORDER BY t.{{_jScheduledAt}}, t.{{_jPartitionKey}}, t.{{_jSequenceNumber}}, t.{{_jId}};
            """;
        return FormattableStringFactory.Create(
            format,
            queueKey.Key, // {0} blocked_partitions queue filter
            queueKey.Key, // {1} outer SELECT queue filter
            now, // {2} VisibleAt
            now, // {3} ScheduledAt
            now // {4} Processing VisibleAt
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
                {{_sPartitionKey}} = {9},
                {{_sRetryIntervals}} = {10},
                {{_sNextRunAt}} = {11},
                {{_sUpdatedAt}} = {12}
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
                {{_sPartitionKey}},
                {{_sRetryIntervals}},
                {{_sNextRunAt}},
                {{_sLastEnqueueAt}},
                {{_sCreatedAt}},
                {{_sUpdatedAt}}
            ) VALUES (
                {13},
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
                {14},
                {15},
                {12}
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
            entity.PartitionKey, // {9}
            retryIntervals, // {10}
            entity.NextRunAt, // {11}
            now, // {12}
            entity.Id, // {13}
            entity.LastEnqueueAt, // {14}
            entity.CreatedAt // {15}
        );
    }
}
