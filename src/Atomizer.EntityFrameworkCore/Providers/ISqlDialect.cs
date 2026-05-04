namespace Atomizer.EntityFrameworkCore.Providers;

internal interface ISqlDialect
{
    FormattableString GetDueJobs(QueueKey queueKey, DateTimeOffset now, int batchSize);
    FormattableString ReleaseLeasedJobs(LeaseToken leaseToken, DateTimeOffset now);
    FormattableString GetDueSchedules(DateTimeOffset now);
    FormattableString UpsertSchedule(AtomizerSchedule schedule, DateTimeOffset now);

    /// <summary>
    /// Returns provider-specific SQL that inserts a partitioned job and atomically assigns
    /// a monotonically increasing <c>SequenceNumber</c> scoped to the job's (queue, partition key).
    /// </summary>
    /// <param name="job">The partitioned job to insert. <see cref="AtomizerJob.PartitionKey"/> must not be null.</param>
    /// <returns>A <see cref="FormattableString"/> ready for <c>ExecuteSqlInterpolatedAsync</c>.</returns>
    FormattableString InsertJobWithSequence(AtomizerJob job);
}
