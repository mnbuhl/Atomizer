using System.Runtime.CompilerServices;
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
        var pgNow = now.ToString("u");
        var c = _jobs.Col;
        return FormattableStringFactory.Create(
            $"""
                SELECT t.*
                FROM {_jobs.Table} AS t
                WHERE {c[nameof(AtomizerJobEntity.QueueKey)]} = '{queueKey}'
                  AND (
                        ( {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending}
                          AND ( {c[nameof(AtomizerJobEntity.VisibleAt)]} IS NULL
                                OR {c[nameof(AtomizerJobEntity.VisibleAt)]} <= '{pgNow}')
                          AND {c[nameof(AtomizerJobEntity.ScheduledAt)]} <= '{pgNow}'
                        )
                        OR
                        ( {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing}
                          AND {c[nameof(AtomizerJobEntity.VisibleAt)]} <= '{pgNow}'
                        )
                      )
                ORDER BY {c[nameof(AtomizerJobEntity.ScheduledAt)]}, {c[nameof(AtomizerJobEntity.Id)]}
                LIMIT {batchSize}
                FOR NO KEY UPDATE SKIP LOCKED;
            """
        );
    }

    public FormattableString ReleaseLeasedJobs(LeaseToken leaseToken, DateTimeOffset now)
    {
        var c = _jobs.Col;
        return FormattableStringFactory.Create(
            $"""
                UPDATE {_jobs.Table}
                SET {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending},
                    {c[nameof(AtomizerJobEntity.LeaseToken)]} = NULL,
                    {c[nameof(AtomizerJobEntity.VisibleAt)]} = NULL,
                    {c[nameof(AtomizerJobEntity.UpdatedAt)]} = '{now:u}'
                WHERE {c[nameof(AtomizerJobEntity.LeaseToken)]} = '{leaseToken.Token}'
                  AND {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing};
            """
        );
    }

    public FormattableString GetDueSchedules(DateTimeOffset now)
    {
        var pgNow = now.ToString("u");
        var c = _schedules.Col;
        return FormattableStringFactory.Create(
            $"""
                SELECT t.*
                FROM {_schedules.Table} AS t
                WHERE {c[nameof(AtomizerScheduleEntity.Enabled)]} = TRUE
                  AND {c[nameof(AtomizerScheduleEntity.NextRunAt)]} <= '{pgNow}'
                ORDER BY {c[nameof(AtomizerScheduleEntity.NextRunAt)]}, {c[
                nameof(AtomizerScheduleEntity.Id)
            ]}
                FOR NO KEY UPDATE SKIP LOCKED;
            """
        );
    }

    public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)
    {
        var entity = schedule.ToEntity();
        var pgNow = DateTimeOffset.UtcNow.ToString("u");
        var c = _schedules.Col;
        return FormattableStringFactory.Create(
            $"""
                INSERT INTO {_schedules.Table} (
                    {c[nameof(AtomizerScheduleEntity.Id)]},
                    {c[nameof(AtomizerScheduleEntity.JobKey)]},
                    {c[nameof(AtomizerScheduleEntity.QueueKey)]},
                    {c[nameof(AtomizerScheduleEntity.PayloadType)]},
                    {c[nameof(AtomizerScheduleEntity.Payload)]},
                    {c[nameof(AtomizerScheduleEntity.Schedule)]},
                    {c[nameof(AtomizerScheduleEntity.TimeZone)]},
                    {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]},
                    {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]},
                    {c[nameof(AtomizerScheduleEntity.Enabled)]},
                    {c[nameof(AtomizerScheduleEntity.RetryIntervals)]},
                    {c[nameof(AtomizerScheduleEntity.NextRunAt)]},
                    {c[nameof(AtomizerScheduleEntity.LastEnqueueAt)]},
                    {c[nameof(AtomizerScheduleEntity.CreatedAt)]},
                    {c[nameof(AtomizerScheduleEntity.UpdatedAt)]}
                ) VALUES (
                    '{entity.Id}',
                    '{entity.JobKey}',
                    '{entity.QueueKey}',
                    '{entity.PayloadType}',
                    '{entity.Payload}',
                    '{entity.Schedule}',
                    '{entity.TimeZone}',
                    {(int)entity.MisfirePolicy},
                    {entity.MaxCatchUp},
                    {(entity.Enabled ? "TRUE" : "FALSE")},
                    '{string.Join(";", Array.ConvertAll(entity.RetryIntervals, ts => (long)ts.TotalMilliseconds))}',
                    '{entity.NextRunAt:u}',
                    {(entity.LastEnqueueAt.HasValue ? $"'{entity.LastEnqueueAt:u}'" : "NULL")},
                    '{entity.CreatedAt:u}',
                    '{pgNow}'
                )
                ON CONFLICT ({c[nameof(AtomizerScheduleEntity.JobKey)]}) DO UPDATE SET
                    {c[nameof(AtomizerScheduleEntity.QueueKey)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.QueueKey)]},
                    {c[nameof(AtomizerScheduleEntity.PayloadType)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.PayloadType)]},
                    {c[nameof(AtomizerScheduleEntity.Payload)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.Payload)]},
                    {c[nameof(AtomizerScheduleEntity.Schedule)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.Schedule)]},
                    {c[nameof(AtomizerScheduleEntity.TimeZone)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.TimeZone)]},
                    {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.MisfirePolicy)]},
                    {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.MaxCatchUp)]},
                    {c[nameof(AtomizerScheduleEntity.Enabled)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.Enabled)]},
                    {c[nameof(AtomizerScheduleEntity.RetryIntervals)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.RetryIntervals)]},
                    {c[nameof(AtomizerScheduleEntity.NextRunAt)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.NextRunAt)]},
                    {c[nameof(AtomizerScheduleEntity.UpdatedAt)]} = EXCLUDED.{c[nameof(AtomizerScheduleEntity.UpdatedAt)]};
            """
        );
    }
}
