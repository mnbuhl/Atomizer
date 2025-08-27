using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

public class MySqlProvider : IDatabaseProviderSql
{
    private readonly EntityMap _jobs;
    private readonly EntityMap _schedules;

    public MySqlProvider(EntityMap jobs, EntityMap schedules)
    {
        _jobs = jobs;
        _schedules = schedules;
    }

    public FormattableString GetDueJobsAsync(QueueKey queueKey, DateTimeOffset now, int batchSize)
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

    public FormattableString ReleaseLeasedJobsAsync(LeaseToken leaseToken)
    {
        var c = _jobs.Col;
        return FormattableStringFactory.Create(
            $"""
                UPDATE {_jobs.Table}
                SET {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending},
                    {c[nameof(AtomizerJobEntity.LeaseToken)]} = NULL,
                    {c[nameof(AtomizerJobEntity.VisibleAt)]} = NULL,
                    {c[nameof(AtomizerJobEntity.UpdatedAt)]} = NOW()
                WHERE {c[nameof(AtomizerJobEntity.LeaseToken)]} = '{leaseToken.Token}'
                  AND {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing};
            """
        );
    }

    public FormattableString GetDueSchedulesAsync(DateTimeOffset now)
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
}
