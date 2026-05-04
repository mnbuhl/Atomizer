using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers;
using Atomizer.EntityFrameworkCore.Providers.Sql;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.Providers;

/// <summary>
/// Unit tests for <see cref="MySqlDialect"/> SQL keyword output.
/// No database container required — EntityMap is built from an in-memory EF Core model.
/// </summary>
public sealed class MySqlDialectTests
{
    private static (EntityMap jobs, EntityMap schedules) BuildMaps()
    {
        var builder = new ModelBuilder();
        builder.AddAtomizerEntities(schema: "atomizer");
        var model = builder.FinalizeModel();
        return (
            EntityMap.Build(model, typeof(AtomizerJobEntity), DatabaseProvider.MySql),
            EntityMap.Build(model, typeof(AtomizerScheduleEntity), DatabaseProvider.MySql)
        );
    }

    [Fact]
    public void GetDueJobs_WhenCalled_ShouldContainForUpdateSkipLocked()
    {
        var (jobs, schedules) = BuildMaps();
        var dialect = new MySqlDialect(jobs, schedules);

        var sql = dialect.GetDueJobs(QueueKey.Default, DateTimeOffset.UtcNow, 10);

        sql.Format.Should().Contain("FOR UPDATE SKIP LOCKED");
        sql.Format.Should().Contain("LIMIT");
    }

    [Fact]
    public void GetDueSchedules_WhenCalled_ShouldContainForUpdateSkipLocked()
    {
        var (jobs, schedules) = BuildMaps();
        var dialect = new MySqlDialect(jobs, schedules);

        var sql = dialect.GetDueSchedules(DateTimeOffset.UtcNow);

        sql.Format.Should().Contain("FOR UPDATE SKIP LOCKED");
    }

    [Fact]
    public void ReleaseLeasedJobs_WhenCalled_ShouldContainUpdateStatement()
    {
        var (jobs, schedules) = BuildMaps();
        var dialect = new MySqlDialect(jobs, schedules);
        var token = new LeaseToken("instance1:*:default:*:aaaaaaaa");

        var sql = dialect.ReleaseLeasedJobs(token, DateTimeOffset.UtcNow);

        sql.Format.Should().Contain("UPDATE");
    }

    [Fact]
    public void UpsertSchedule_WhenCalled_ShouldContainOnDuplicateKeyUpdate()
    {
        var (jobs, schedules) = BuildMaps();
        var dialect = new MySqlDialect(jobs, schedules);
        var schedule = AtomizerSchedule.Create(
            new JobKey("test-key"),
            QueueKey.Default,
            typeof(object),
            "{}",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            DateTimeOffset.UtcNow
        );

        var sql = dialect.UpsertSchedule(schedule, DateTimeOffset.UtcNow);

        sql.Format.Should().Contain("ON DUPLICATE KEY UPDATE");
        sql.Format.Should().Contain("PartitionKey");
    }
}
