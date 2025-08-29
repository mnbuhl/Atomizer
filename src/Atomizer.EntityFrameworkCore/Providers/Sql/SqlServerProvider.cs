using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

public class SqlServerProvider : IDatabaseProviderSql
{
    private readonly EntityMap _jobs;
    private readonly EntityMap _schedules;

    public SqlServerProvider(EntityMap jobs, EntityMap schedules)
    {
        _jobs = jobs;
        _schedules = schedules;
    }

    public FormattableString GetDueJobsAsync(QueueKey queueKey, DateTimeOffset now, int batchSize)
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

    public FormattableString ReleaseLeasedJobsAsync(LeaseToken leaseToken, DateTimeOffset now)
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

    public FormattableString GetDueSchedulesAsync(DateTimeOffset now)
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
}
