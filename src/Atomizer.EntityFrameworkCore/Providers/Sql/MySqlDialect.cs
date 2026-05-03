using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

internal sealed class MySqlDialect : ISqlDialect
{
    private readonly EntityMap _jobs;
    private readonly EntityMap _schedules;

    public MySqlDialect(EntityMap jobs, EntityMap schedules)
    {
        _jobs = jobs;
        _schedules = schedules;
    }

    public FormattableString GetDueJobs(QueueKey queueKey, DateTimeOffset now, int batchSize)
    {
        var mySqlNow = now.ToString("yyyy-MM-dd HH:mm:ss");
        var c = _jobs.Col;
        return FormattableStringFactory.Create(
            $"""
                SELECT t.*
                FROM {_jobs.Table} AS t
                WHERE {c[nameof(AtomizerJobEntity.QueueKey)]} = '{queueKey}'
                  AND (
                        ( {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending}
                          AND ( {c[nameof(AtomizerJobEntity.VisibleAt)]} IS NULL
                                OR {c[nameof(AtomizerJobEntity.VisibleAt)]} <= '{mySqlNow}')
                          AND {c[nameof(AtomizerJobEntity.ScheduledAt)]} <= '{mySqlNow}'
                        )
                        OR
                        ( {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing}
                          AND {c[nameof(AtomizerJobEntity.VisibleAt)]} <= '{mySqlNow}'
                        )
                      )
                ORDER BY {c[nameof(AtomizerJobEntity.ScheduledAt)]}, {c[nameof(AtomizerJobEntity.Id)]}
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED;
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
                    {c[nameof(AtomizerJobEntity.UpdatedAt)]} = '{now:yyyy-MM-dd HH:mm:ss}'
                WHERE {c[nameof(AtomizerJobEntity.LeaseToken)]} = '{leaseToken.Token}'
                  AND {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing};
            """
        );
    }

    public FormattableString GetDueSchedules(DateTimeOffset now)
    {
        var mySqlNow = now.ToString("yyyy-MM-dd HH:mm:ss");
        var c = _schedules.Col;
        return FormattableStringFactory.Create(
            $"""
                SELECT *
                FROM {_schedules.Table}
                WHERE {c[nameof(AtomizerScheduleEntity.NextRunAt)]} <= '{mySqlNow}'
                  AND {c[nameof(AtomizerScheduleEntity.Enabled)]} = TRUE
                ORDER BY {c[nameof(AtomizerScheduleEntity.NextRunAt)]}, {c[nameof(AtomizerScheduleEntity.Id)]}
                FOR UPDATE SKIP LOCKED;
            """
        );
    }

    public FormattableString UpsertScheduleAsync(AtomizerSchedule schedule)
    {
        var entity = schedule.ToEntity();
        var mySqlNow = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
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
                    '{entity.NextRunAt.ToString("yyyy-MM-dd HH:mm:ss")}',
                    {(entity.LastEnqueueAt.HasValue ? $"'{entity.LastEnqueueAt.Value.ToString("yyyy-MM-dd HH:mm:ss")}'" : "NULL")},
                    '{entity.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")}',
                    '{mySqlNow}'
                )
                ON DUPLICATE KEY UPDATE
                    {c[nameof(AtomizerScheduleEntity.QueueKey)]} = VALUES({c[nameof(AtomizerScheduleEntity.QueueKey)]}),
                    {c[nameof(AtomizerScheduleEntity.PayloadType)]} = VALUES({c[nameof(AtomizerScheduleEntity.PayloadType)]}),
                    {c[nameof(AtomizerScheduleEntity.Payload)]} = VALUES({c[nameof(AtomizerScheduleEntity.Payload)]}),
                    {c[nameof(AtomizerScheduleEntity.Schedule)]} = VALUES({c[nameof(AtomizerScheduleEntity.Schedule)]}),
                    {c[nameof(AtomizerScheduleEntity.TimeZone)]} = VALUES({c[nameof(AtomizerScheduleEntity.TimeZone)]}),
                    {c[nameof(AtomizerScheduleEntity.MisfirePolicy)]} = VALUES({c[nameof(AtomizerScheduleEntity.MisfirePolicy)]}),
                    {c[nameof(AtomizerScheduleEntity.MaxCatchUp)]} = VALUES({c[nameof(AtomizerScheduleEntity.MaxCatchUp)]}),
                    {c[nameof(AtomizerScheduleEntity.Enabled)]} = VALUES({c[nameof(AtomizerScheduleEntity.Enabled)]}),
                    {c[nameof(AtomizerScheduleEntity.RetryIntervals)]} = VALUES({c[nameof(AtomizerScheduleEntity.RetryIntervals)]}),
                    {c[nameof(AtomizerScheduleEntity.NextRunAt)]} = VALUES({c[nameof(AtomizerScheduleEntity.NextRunAt)]}),
                    {c[nameof(AtomizerScheduleEntity.UpdatedAt)]} = '{mySqlNow}';
            """
        );
    }
}
