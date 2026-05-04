namespace Atomizer.EntityFrameworkCore.Providers;

internal interface ISqlDialect
{
    FormattableString GetDueJobs(QueueKey queueKey, DateTimeOffset now, int batchSize);
    FormattableString ReleaseLeasedJobs(LeaseToken leaseToken, DateTimeOffset now);
    FormattableString ReleaseLeasedJobsByInstanceId(string instanceId, DateTimeOffset now);
    FormattableString DeleteStaleServer(string instanceId, DateTimeOffset staleBefore);
    FormattableString GetDueSchedules(DateTimeOffset now);
    FormattableString UpsertScheduleAsync(AtomizerSchedule schedule, DateTimeOffset now);
}
