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
        throw new NotImplementedException();
    }

    public FormattableString ReleaseLeasedJobsAsync(LeaseToken leaseToken)
    {
        throw new NotImplementedException();
    }

    public FormattableString GetDueSchedulesAsync(DateTimeOffset now)
    {
        throw new NotImplementedException();
    }

    public FormattableString ReleaseLeasedSchedulesAsync(LeaseToken leaseToken)
    {
        throw new NotImplementedException();
    }
}
