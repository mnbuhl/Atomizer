using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

public class OracleProvider : IDatabaseProviderSql
{
    private readonly EntityMap _jobs;
    private readonly EntityMap _schedules;

    public OracleProvider(EntityMap jobs, EntityMap schedules)
    {
        _jobs = jobs;
        _schedules = schedules;
    }

    public FormattableString GetDueJobsAsync(QueueKey queueKey, DateTimeOffset now, int batchSize)
    {
        var oracleNow = now.ToString("u").Replace(' ', 'T');
        var c = _jobs.Col;
        return FormattableStringFactory.Create(
            $"""
                SELECT *
                FROM (
                    SELECT t.*
                    FROM {_jobs.Table} t
                    WHERE {c[nameof(AtomizerJobEntity.QueueKey)]} = '{queueKey}'
                      AND (
                            ( {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending}
                              AND ( {c[nameof(AtomizerJobEntity.VisibleAt)]} IS NULL
                                    OR {c[nameof(AtomizerJobEntity.VisibleAt)]} <= '{oracleNow}')
                              AND {c[nameof(AtomizerJobEntity.ScheduledAt)]} <= '{oracleNow}'
                            )
                            OR
                            ( {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing}
                              AND {c[nameof(AtomizerJobEntity.VisibleAt)]} <= '{oracleNow}'
                            )
                          )
                    ORDER BY {c[nameof(AtomizerJobEntity.ScheduledAt)]}, {c[nameof(AtomizerJobEntity.Id)]}
                )
                WHERE ROWNUM <= {batchSize}
                FOR UPDATE SKIP LOCKED
            """
        );
    }

    public FormattableString ReleaseLeasedJobsAsync(LeaseToken leaseToken, DateTimeOffset now)
    {
        var oracleNow = now.ToString("u").Replace(' ', 'T');
        var c = _jobs.Col;
        return FormattableStringFactory.Create(
            $"""
                UPDATE {_jobs.Table}
                SET {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Pending},
                    {c[nameof(AtomizerJobEntity.LeaseToken)]} = NULL,
                    {c[nameof(AtomizerJobEntity.VisibleAt)]} = NULL,
                    {c[nameof(AtomizerJobEntity.UpdatedAt)]} = '{oracleNow}'
                WHERE {c[nameof(AtomizerJobEntity.LeaseToken)]} = '{leaseToken.Token}'
                  AND {c[nameof(AtomizerJobEntity.Status)]} = {(int)AtomizerEntityJobStatus.Processing}
            """
        );
    }

    public FormattableString GetDueSchedulesAsync(DateTimeOffset now)
    {
        // for update skip locked
        var oracleNow = now.ToString("u").Replace(' ', 'T');
        var c = _schedules.Col;
        return FormattableStringFactory.Create(
            $"""
                SELECT *
                FROM {_schedules.Table} t
                WHERE {c[nameof(AtomizerScheduleEntity.NextRunAt)]} <= '{oracleNow}'
                  AND {c[nameof(AtomizerScheduleEntity.Enabled)]} = 1
                ORDER BY {c[nameof(AtomizerScheduleEntity.NextRunAt)]}, {c[nameof(AtomizerScheduleEntity.Id)]}
                FOR UPDATE SKIP LOCKED
            """
        );
    }
}
