using Atomizer;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers;
using Atomizer.EntityFrameworkCore.Providers.Sql;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.Providers;

/// <summary>
/// Unit tests for <see cref="SqlServerDialect"/> SQL keyword output.
/// No database container required — EntityMap is built from an in-memory EF Core model.
/// </summary>
public sealed class SqlServerDialectTests
{
    private static (EntityMap jobs, EntityMap schedules) BuildMaps()
    {
        var builder = new ModelBuilder();
        builder.AddAtomizerEntities(schema: "atomizer");
        var model = builder.FinalizeModel();
        return (
            EntityMap.Build(model, typeof(AtomizerJobEntity), DatabaseProvider.SqlServer),
            EntityMap.Build(model, typeof(AtomizerScheduleEntity), DatabaseProvider.SqlServer)
        );
    }

    [Fact]
    public void GetDueJobs_WhenCalled_ShouldContainWithUpdlockReadpastRowlock()
    {
        var (jobs, schedules) = BuildMaps();
        var dialect = new SqlServerDialect(jobs, schedules);

        var sql = dialect.GetDueJobs(QueueKey.Default, DateTimeOffset.UtcNow, 10);

        sql.Format.Should().Contain("WITH (UPDLOCK, READPAST, ROWLOCK)");
        sql.Format.Should().Contain("TOP(");
    }

    [Fact]
    public void GetDueSchedules_WhenCalled_ShouldContainWithUpdlockReadpastRowlock()
    {
        var (jobs, schedules) = BuildMaps();
        var dialect = new SqlServerDialect(jobs, schedules);

        var sql = dialect.GetDueSchedules(DateTimeOffset.UtcNow);

        sql.Format.Should().Contain("WITH (UPDLOCK, READPAST, ROWLOCK)");
    }

    [Fact]
    public void ReleaseLeasedJobs_WhenCalled_ShouldContainUpdateStatement()
    {
        var (jobs, schedules) = BuildMaps();
        var dialect = new SqlServerDialect(jobs, schedules);
        var token = new LeaseToken("instance1:*:default:*:aaaaaaaa");

        var sql = dialect.ReleaseLeasedJobs(token, DateTimeOffset.UtcNow);

        sql.Format.Should().Contain("UPDATE");
    }

    [Fact]
    public void UpsertScheduleAsync_WhenCalled_ShouldContainMergeWithHoldlock()
    {
        var (jobs, schedules) = BuildMaps();
        var dialect = new SqlServerDialect(jobs, schedules);
        var schedule = AtomizerSchedule.Create(
            new JobKey("test-key"),
            QueueKey.Default,
            typeof(object),
            "{}",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            DateTimeOffset.UtcNow
        );

        var sql = dialect.UpsertScheduleAsync(schedule, DateTimeOffset.UtcNow);

        sql.Format.Should().Contain("MERGE");
        sql.Format.Should().Contain("WITH (HOLDLOCK)");
    }
}
