using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using Atomizer.Tests.Utilities;
using AwesomeAssertions;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Dashboard;

public abstract class EntityFrameworkCoreStorageMonitoringTests<TDbContext> : IAsyncLifetime
    where TDbContext : TestDbContext
{
    protected readonly IAtomizerClock Clock = Substitute.For<IAtomizerClock>();
    protected abstract TDbContext CreateDbContext();

    protected IAtomizerStorage CreateStorage() =>
        new EntityFrameworkCoreStorage<TDbContext>(
            CreateDbContext(),
            new EntityFrameworkCoreJobStorageOptions(),
            Substitute.For<TestableLogger<EntityFrameworkCoreStorage<TDbContext>>>(),
            Clock
        );

    public abstract ValueTask InitializeAsync();

    public abstract ValueTask DisposeAsync();

    [Fact]
    public async Task GetJobsAsync_WhenJobsExist_ShouldReturnPagedResults()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.UtcNow.Returns(now);

        await using var db = CreateDbContext();
        db.Set<AtomizerJobEntity>()
            .AddRange(
                AtomizerJob
                    .Create(QueueKey.Default, typeof(object), "{}", now.AddMinutes(-3), now.AddMinutes(-3))
                    .ToEntity(),
                AtomizerJob
                    .Create(QueueKey.Default, typeof(object), "{}", now.AddMinutes(-2), now.AddMinutes(-2))
                    .ToEntity(),
                AtomizerJob
                    .Create(QueueKey.Default, typeof(object), "{}", now.AddMinutes(-1), now.AddMinutes(-1))
                    .ToEntity()
            );
        await db.SaveChangesAsync();

        var result = await CreateStorage().GetJobsAsync(new JobQuery { Skip = 0, Take = 2 }, CancellationToken.None);

        result.TotalCount.Should().Be(3);
        result.Items.Count.Should().Be(2);
        result.Take.Should().Be(2);
        result.Items[0].CreatedAt.Should().BeCloseTo(now.AddMinutes(-1), TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task GetJobsAsync_WhenFilterByStatus_ShouldReturnMatchingJobs()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.UtcNow.Returns(now);

        await using var db = CreateDbContext();
        db.Set<AtomizerJobEntity>()
            .AddRange(
                AtomizerJob.Create(QueueKey.Default, typeof(object), "{}", now, now).ToEntity(),
                CreateFailedJobEntity(now)
            );
        await db.SaveChangesAsync();

        var result = await CreateStorage()
            .GetJobsAsync(
                new JobQuery
                {
                    Skip = 0,
                    Take = 100,
                    Statuses = [AtomizerJobStatus.Failed],
                },
                CancellationToken.None
            );

        result.TotalCount.Should().Be(1);
        result.Items[0].Status.Should().Be(AtomizerJobStatus.Failed);
    }

    [Fact]
    public async Task GetJobsAsync_WhenTakeExceedsMax_ShouldCapAt500()
    {
        Clock.UtcNow.Returns(DateTimeOffset.UtcNow);

        var result = await CreateStorage().GetJobsAsync(new JobQuery { Skip = 0, Take = 9999 }, CancellationToken.None);

        result.Take.Should().Be(500);
    }

    [Fact]
    public async Task GetSchedulesAsync_WhenSchedulesExist_ShouldReturnAll()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.UtcNow.Returns(now);

        var schedule = AtomizerSchedule.Create(
            new JobKey("test-job"),
            QueueKey.Default,
            typeof(object),
            "{}",
            Schedule.Cron("0 * * * *"),
            TimeZoneInfo.Utc,
            now
        );

        await using var db = CreateDbContext();
        db.Set<AtomizerScheduleEntity>().Add(schedule.ToEntity());
        await db.SaveChangesAsync();

        var result = await CreateStorage().GetSchedulesAsync(CancellationToken.None);

        result.Count.Should().Be(1);
        result[0].JobKey.ToString().Should().Be("test-job");
    }

    [Fact]
    public async Task GetActiveServersAsync_WhenServerHasRecentHeartbeat_ShouldReturnIt()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.UtcNow.Returns(now);

        await using var db = CreateDbContext();
        db.Set<AtomizerActiveServerEntity>()
            .Add(new AtomizerActiveServerEntity { InstanceId = "server-1", LastHeartbeatAt = now.AddMinutes(-1) });
        await db.SaveChangesAsync();

        var result = await CreateStorage().GetActiveServersAsync(CancellationToken.None);

        result.Count.Should().Be(1);
        result[0].InstanceId.Should().Be("server-1");
    }

    [Fact]
    public async Task GetActiveServersAsync_WhenServerHeartbeatIsStale_ShouldExcludeIt()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.UtcNow.Returns(now);

        await using var db = CreateDbContext();
        db.Set<AtomizerActiveServerEntity>()
            .Add(new AtomizerActiveServerEntity { InstanceId = "stale-server", LastHeartbeatAt = now.AddMinutes(-10) });
        await db.SaveChangesAsync();

        var result = await CreateStorage().GetActiveServersAsync(CancellationToken.None);

        result.Count.Should().Be(0);
    }

    [Fact]
    public async Task GetQueueStatsAsync_ShouldReturnCorrectCountsPerQueue()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.UtcNow.Returns(now);

        var defaultQueue = QueueKey.Default;
        var otherQueue = new QueueKey("other");

        await using var db = CreateDbContext();
        db.Set<AtomizerJobEntity>()
            .AddRange(
                AtomizerJob.Create(defaultQueue, typeof(object), "{}", now, now).ToEntity(),
                AtomizerJob.Create(defaultQueue, typeof(object), "{}", now, now).ToEntity(),
                AtomizerJob.Create(otherQueue, typeof(object), "{}", now, now).ToEntity(),
                CreateFailedJobEntity(now, defaultQueue)
            );
        await db.SaveChangesAsync();

        var result = await CreateStorage().GetQueueStatsAsync(CancellationToken.None);

        var defaultStats = result.Single(s => s.QueueKey == defaultQueue);
        defaultStats.Pending.Should().Be(2);
        defaultStats.Failed.Should().Be(1);

        var otherStats = result.Single(s => s.QueueKey == otherQueue);
        otherStats.Pending.Should().Be(1);
    }

    [Fact]
    public async Task GetJobStatusCountsAsync_WhenQueryHasStatusFilter_ShouldIgnoreStatusAndApplyOtherFilters()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.UtcNow.Returns(now);

        var defaultQueue = QueueKey.Default;
        var otherQueue = new QueueKey("other");

        await using var db = CreateDbContext();
        db.Set<AtomizerJobEntity>()
            .AddRange(
                CreateJobEntity(now, AtomizerEntityJobStatus.Pending, defaultQueue),
                CreateJobEntity(now, AtomizerEntityJobStatus.Processing, defaultQueue),
                CreateJobEntity(now, AtomizerEntityJobStatus.Failed, defaultQueue),
                CreateJobEntity(now, AtomizerEntityJobStatus.Completed, otherQueue)
            );
        await db.SaveChangesAsync();

        var result = await CreateStorage()
            .GetJobStatusCountsAsync(
                new JobQuery { QueueKey = defaultQueue, Statuses = [AtomizerJobStatus.Pending] },
                CancellationToken.None
            );

        result.Pending.Should().Be(1);
        result.Processing.Should().Be(1);
        result.Completed.Should().Be(0);
        result.Failed.Should().Be(1);
    }

    private static AtomizerJobEntity CreateFailedJobEntity(DateTimeOffset now, QueueKey? queueKey = null) =>
        new AtomizerJobEntity
        {
            Id = Guid.NewGuid(),
            QueueKey = (queueKey ?? QueueKey.Default).ToString(),
            PayloadType = typeof(object).AssemblyQualifiedName!,
            Payload = "{}",
            ScheduledAt = now,
            Status = AtomizerEntityJobStatus.Failed,
            Attempts = 1,
            RetryIntervals = [],
            CreatedAt = now,
            UpdatedAt = now,
            FailedAt = now,
        };

    private static AtomizerJobEntity CreateJobEntity(
        DateTimeOffset now,
        AtomizerEntityJobStatus status,
        QueueKey queueKey
    ) =>
        new AtomizerJobEntity
        {
            Id = Guid.NewGuid(),
            QueueKey = queueKey.ToString(),
            PayloadType = typeof(object).AssemblyQualifiedName!,
            Payload = "{}",
            ScheduledAt = now,
            Status = status,
            Attempts = status == AtomizerEntityJobStatus.Pending ? 0 : 1,
            RetryIntervals = [],
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = status == AtomizerEntityJobStatus.Completed ? now : null,
            FailedAt = status == AtomizerEntityJobStatus.Failed ? now : null,
            LeaseToken = status == AtomizerEntityJobStatus.Processing ? "server-1:*:default:*:lease" : null,
        };
}
