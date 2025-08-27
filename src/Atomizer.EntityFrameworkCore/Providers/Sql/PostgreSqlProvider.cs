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

    public FormattableString ReleaseLeasedJobsAsync(LeaseToken leaseToken)
    {
        var c = _jobs.Col;
        return FormattableStringFactory.Create(
            $"""
                UPDATE {_jobs.Table}
                SET {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending},
                    {c[nameof(AtomizerJobEntity.LeaseToken)]} = NULL,
                    {c[nameof(AtomizerJobEntity.VisibleAt)]} = NULL,
                    {c[nameof(AtomizerJobEntity.UpdatedAt)]} = NOW() AT TIME ZONE 'UTC'
                WHERE {c[nameof(AtomizerJobEntity.LeaseToken)]} = '{leaseToken.Token}'
                  AND {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing};
            """
        );
    }

    public FormattableString GetDueSchedulesAsync(DateTimeOffset now)
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
}
