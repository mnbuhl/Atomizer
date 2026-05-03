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
        var sqlServerNow = now.ToString("u");
        var c = _jobs.Col;
        return FormattableStringFactory.Create(
            $"""
                SELECT TOP({batchSize}) t.*
                FROM {_jobs.Table} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE {c[nameof(AtomizerJobEntity.QueueKey)]} = '{queueKey}'
                  AND (
                        ( {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending}
                          AND ( {c[nameof(AtomizerJobEntity.VisibleAt)]} IS NULL
                                OR {c[nameof(AtomizerJobEntity.VisibleAt)]} <= '{sqlServerNow}')
                          AND {c[nameof(AtomizerJobEntity.ScheduledAt)]} <= '{sqlServerNow}'
                        )
                        OR
                        ( {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing}
                          AND {c[nameof(AtomizerJobEntity.VisibleAt)]} <= '{sqlServerNow}'
                        )
                      )
                ORDER BY {c[nameof(AtomizerJobEntity.ScheduledAt)]}, {c[nameof(AtomizerJobEntity.Id)]};
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
        var sqlServerNow = now.ToString("u");
        var c = _schedules.Col;
        return FormattableStringFactory.Create(
            $"""
                SELECT t.*
                FROM {_schedules.Table} AS t WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE {c[nameof(AtomizerScheduleEntity.NextRunAt)]} <= '{sqlServerNow}'
                  AND {c[nameof(AtomizerScheduleEntity.Enabled)]} = 1
                ORDER BY {c[nameof(AtomizerScheduleEntity.NextRunAt)]}, {c[nameof(AtomizerScheduleEntity.Id)]};
            """
        );
    }

    public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)
    {
        var entity = schedule.ToEntity();
        var sqlServerNow = DateTimeOffset.UtcNow.ToString("u");
        var c = _schedules.Col;
        return FormattableStringFactory.Create(
            $"""
                MERGE {_schedules.Table} WITH (HOLDLOCK) AS target
                USING (SELECT '{entity.JobKey}') AS src ({c[nameof(AtomizerScheduleEntity.JobKey)]})
                ON target.{c[nameof(AtomizerScheduleEntity.JobKey)]} = src.{c[nameof(AtomizerScheduleEntity.JobKey)]}
                WHEN MATCHED THEN UPDATE SET
                    {c[nameof(AtomizerScheduleEntity.QueueKey)]} = '{entity.QueueKey}',
                    {c[nameof(AtomizerScheduleEntity.PayloadType)]} = '{entity.PayloadType}',
                    {c[nameof(AtomizerScheduleEntity.Payload)]} = '{entity.Payload}',
                    {c[nameof(AtomizerScheduleEntity.Schedule)]} = '{entity.Schedule}',
                    {c[nameof(AtomizerScheduleEntity.TimeZone)]} = '{entity.TimeZone}',
                    {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]} = {(int)entity.MisfirePolicy},
                    {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]} = {entity.MaxCatchUp},
                    {c[nameof(AtomizerScheduleEntity.Enabled)]} = {(entity.Enabled ? 1 : 0)},
                    {c[nameof(AtomizerScheduleEntity.RetryIntervals)]} = '{string.Join(";", Array.ConvertAll(entity.RetryIntervals, ts => (long)ts.TotalMilliseconds))}',
                    {c[nameof(AtomizerScheduleEntity.NextRunAt)]} = '{entity.NextRunAt:u}',
                    {c[nameof(AtomizerScheduleEntity.UpdatedAt)]} = '{sqlServerNow}'
                WHEN NOT MATCHED THEN INSERT (
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
                    {(entity.Enabled ? 1 : 0)},
                    '{string.Join(";", Array.ConvertAll(entity.RetryIntervals, ts => (long)ts.TotalMilliseconds))}',
                    '{entity.NextRunAt:u}',
                    {(entity.LastEnqueueAt.HasValue ? $"'{entity.LastEnqueueAt:u}'" : "NULL")},
                    '{entity.CreatedAt:u}',
                    '{sqlServerNow}'
                );
            """
        );
    }
}
