namespace Atomizer.EntityFrameworkCore.Providers;

public interface IDatabaseProviderSql
{
    FormattableString GetDueJobsAsync(QueueKey queueKey, DateTimeOffset now, int batchSize);
    FormattableString GetDueSchedulesAsync(DateTimeOffset now);
}
