namespace Atomizer.EntityFrameworkCore.Providers;

public interface IDatabaseProviderSql
{
    FormattableString GetDueJobsAsync(QueueKey queueKey, DateTimeOffset now, int batchSize);
    FormattableString ReleaseLeasedJobsAsync(LeaseToken leaseToken, DateTimeOffset now);
    FormattableString GetDueSchedulesAsync(DateTimeOffset now);
}
