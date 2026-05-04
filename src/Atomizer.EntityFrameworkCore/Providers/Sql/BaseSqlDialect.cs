using System.Runtime.CompilerServices;
using Atomizer.EntityFrameworkCore.Entities;

namespace Atomizer.EntityFrameworkCore.Providers.Sql;

internal abstract class BaseSqlDialect : ISqlDialect
{
    // Job table and columns
    protected readonly string _jTable;
    protected readonly string _jId;
    protected readonly string _jQueueKey;
    protected readonly string _jPayloadType;
    protected readonly string _jPayload;
    protected readonly string _jScheduledAt;
    protected readonly string _jVisibleAt;
    protected readonly string _jStatus;
    protected readonly string _jAttempts;
    protected readonly string _jRetryIntervals;
    protected readonly string _jCreatedAt;
    protected readonly string _jUpdatedAt;
    protected readonly string _jLeaseToken;
    protected readonly string _jScheduleJobKey;
    protected readonly string _jIdempotencyKey;
    protected readonly string _jPartitionKey;
    protected readonly string _jSequenceNumber;

    // Schedule table and columns
    protected readonly string _sTable;
    protected readonly string _sId;
    protected readonly string _sJobKey;
    protected readonly string _sQueueKey;
    protected readonly string _sPayloadType;
    protected readonly string _sPayload;
    protected readonly string _sSchedule;
    protected readonly string _sTimeZone;
    protected readonly string _sMisfirePolicy;
    protected readonly string _sMaxCatchUp;
    protected readonly string _sEnabled;
    protected readonly string _sRetryIntervals;
    protected readonly string _sNextRunAt;
    protected readonly string _sLastEnqueueAt;
    protected readonly string _sCreatedAt;
    protected readonly string _sUpdatedAt;

    protected readonly int _statusPending = (int)AtomizerEntityJobStatus.Pending;
    protected readonly int _statusProcessing = (int)AtomizerEntityJobStatus.Processing;

    protected BaseSqlDialect(EntityMap jobs, EntityMap schedules)
    {
        var jc = jobs.Col;
        _jTable = jobs.Table;
        _jId = jc[nameof(AtomizerJobEntity.Id)];
        _jQueueKey = jc[nameof(AtomizerJobEntity.QueueKey)];
        _jPayloadType = jc[nameof(AtomizerJobEntity.PayloadType)];
        _jPayload = jc[nameof(AtomizerJobEntity.Payload)];
        _jScheduledAt = jc[nameof(AtomizerJobEntity.ScheduledAt)];
        _jVisibleAt = jc[nameof(AtomizerJobEntity.VisibleAt)];
        _jStatus = jc[nameof(AtomizerJobEntity.Status)];
        _jAttempts = jc[nameof(AtomizerJobEntity.Attempts)];
        _jRetryIntervals = jc[nameof(AtomizerJobEntity.RetryIntervals)];
        _jCreatedAt = jc[nameof(AtomizerJobEntity.CreatedAt)];
        _jUpdatedAt = jc[nameof(AtomizerJobEntity.UpdatedAt)];
        _jLeaseToken = jc[nameof(AtomizerJobEntity.LeaseToken)];
        _jScheduleJobKey = jc[nameof(AtomizerJobEntity.ScheduleJobKey)];
        _jIdempotencyKey = jc[nameof(AtomizerJobEntity.IdempotencyKey)];
        _jPartitionKey = jc[nameof(AtomizerJobEntity.PartitionKey)];
        _jSequenceNumber = jc[nameof(AtomizerJobEntity.SequenceNumber)];

        var sc = schedules.Col;
        _sTable = schedules.Table;
        _sId = sc[nameof(AtomizerScheduleEntity.Id)];
        _sJobKey = sc[nameof(AtomizerScheduleEntity.JobKey)];
        _sQueueKey = sc[nameof(AtomizerScheduleEntity.QueueKey)];
        _sPayloadType = sc[nameof(AtomizerScheduleEntity.PayloadType)];
        _sPayload = sc[nameof(AtomizerScheduleEntity.Payload)];
        _sSchedule = sc[nameof(AtomizerScheduleEntity.Schedule)];
        _sTimeZone = sc[nameof(AtomizerScheduleEntity.TimeZone)];
        _sMisfirePolicy = sc[nameof(AtomizerScheduleEntity.MisfirePolicy)];
        _sMaxCatchUp = sc[nameof(AtomizerScheduleEntity.MaxCatchUp)];
        _sEnabled = sc[nameof(AtomizerScheduleEntity.Enabled)];
        _sRetryIntervals = sc[nameof(AtomizerScheduleEntity.RetryIntervals)];
        _sNextRunAt = sc[nameof(AtomizerScheduleEntity.NextRunAt)];
        _sLastEnqueueAt = sc[nameof(AtomizerScheduleEntity.LastEnqueueAt)];
        _sCreatedAt = sc[nameof(AtomizerScheduleEntity.CreatedAt)];
        _sUpdatedAt = sc[nameof(AtomizerScheduleEntity.UpdatedAt)];
    }

    protected static string SerializeIntervals(TimeSpan[] intervals) =>
        string.Join(";", Array.ConvertAll(intervals, ts => (long)ts.TotalMilliseconds));

    public FormattableString ReleaseLeasedJobs(LeaseToken leaseToken, DateTimeOffset now)
    {
        var format =
            $$"""
            UPDATE {{_jTable}}
            SET {{_jStatus}} = {{_statusPending}},
                {{_jLeaseToken}} = NULL,
                {{_jVisibleAt}} = NULL,
                {{_jUpdatedAt}} = {0}
            WHERE {{_jLeaseToken}} = {1}
              AND {{_jStatus}} = {{_statusProcessing}};
            """;
        return FormattableStringFactory.Create(format, now, leaseToken.Token);
    }

    public abstract FormattableString GetDueJobs(QueueKey queueKey, DateTimeOffset now, int batchSize);
    public abstract FormattableString InsertJobWithSequence(AtomizerJob job);
    public abstract FormattableString GetDueSchedules(DateTimeOffset now);
    public abstract FormattableString UpsertScheduleAsync(AtomizerSchedule schedule, DateTimeOffset now);
}
