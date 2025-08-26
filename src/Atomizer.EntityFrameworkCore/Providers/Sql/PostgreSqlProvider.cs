using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

public class PostgreSqlProvider : IDatabaseProviderSql
{
    private readonly EntityMap _jobs;
    private readonly EntityMap _schedules;

    public PostgreSqlProvider(EntityMap jobs, EntityMap schedules)
    {
        _jobs = jobs;
        _schedules = schedules;
    }

    public FormattableString GetDueJobsAsync(QueueKey queueKey, DateTimeOffset now, int batchSize)
    {
        var pgNow = now.ToString("u");
        return FormattableStringFactory.Create(
            $"""
                SELECT t.*
                FROM {_jobs.Table} AS t
                WHERE {_jobs.Col[nameof(AtomizerJobEntity.QueueKey)]} = '{queueKey}'
                  AND (
                        ( {_jobs.Col[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending}
                          AND ( {_jobs.Col[nameof(AtomizerJobEntity.VisibleAt)]} IS NULL
                                OR {_jobs.Col[nameof(AtomizerJobEntity.VisibleAt)]} <= '{pgNow}')
                          AND {_jobs.Col[nameof(AtomizerJobEntity.ScheduledAt)]} <= '{pgNow}'
                        )
                        OR
                        ( {_jobs.Col[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing}
                          AND {_jobs.Col[nameof(AtomizerJobEntity.VisibleAt)]} <= '{pgNow}'
                        )
                      )
                ORDER BY {_jobs.Col[nameof(AtomizerJobEntity.ScheduledAt)]}, {_jobs.Col[nameof(AtomizerJobEntity.Id)]}
                LIMIT {batchSize}
                FOR NO KEY UPDATE SKIP LOCKED;
            """
        );
    }

    public FormattableString ReleaseLeasedJobsAsync(LeaseToken leaseToken)
    {
        return FormattableStringFactory.Create(
            $"""
                UPDATE {_jobs.Table}
                SET {_jobs.Col[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending},
                    {_jobs.Col[nameof(AtomizerJobEntity.LeaseToken)]} = NULL,
                    {_jobs.Col[nameof(AtomizerJobEntity.VisibleAt)]} = NULL,
                    {_jobs.Col[nameof(AtomizerJobEntity.UpdatedAt)]} = NOW() AT TIME ZONE 'UTC'
                WHERE {_jobs.Col[nameof(AtomizerJobEntity.LeaseToken)]} = '{leaseToken.Token}'
                  AND {_jobs.Col[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing};
            """
        );
    }

    public FormattableString GetDueSchedulesAsync(DateTimeOffset now)
    {
        var pgNow = now.ToString("u");
        return FormattableStringFactory.Create(
            $"""
                SELECT t.*
                FROM {_schedules.Table} AS t
                WHERE {_schedules.Col[nameof(AtomizerScheduleEntity.Enabled)]} = TRUE
                  AND {_schedules.Col[nameof(AtomizerScheduleEntity.NextRunAt)]} <= '{pgNow}'
                ORDER BY {_schedules.Col[nameof(AtomizerScheduleEntity.NextRunAt)]}, {_schedules.Col[
                nameof(AtomizerScheduleEntity.Id)
            ]}
                FOR NO KEY UPDATE SKIP LOCKED;
            """
        );
    }
}
