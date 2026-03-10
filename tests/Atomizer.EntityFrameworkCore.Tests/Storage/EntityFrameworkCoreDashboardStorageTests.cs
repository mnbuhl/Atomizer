using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using Atomizer.Tests.Utilities;
using Atomizer.Tests.Utilities.TestJobs;
using AwesomeAssertions;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage;

/// <summary>
/// Integration tests for the dashboard-specific read methods on
/// <see cref="EntityFrameworkCoreStorage{TDbContext}"/>:
/// <see cref="Atomizer.Abstractions.IAtomizerDashboardStorage.GetQueueStatsAsync"/>,
/// <see cref="Atomizer.Abstractions.IAtomizerDashboardStorage.GetRecentJobsAsync"/>,
/// <see cref="Atomizer.Abstractions.IAtomizerDashboardStorage.GetAllSchedulesAsync"/>,
/// <see cref="Atomizer.Abstractions.IAtomizerDashboardStorage.GetJobByIdAsync"/>, and
/// <see cref="Atomizer.Abstractions.IAtomizerDashboardStorage.GetLastJobForScheduleAsync"/>.
/// </summary>
public abstract class EntityFrameworkCoreDashboardStorageTests : IAsyncLifetime
{
    private readonly Func<TestDbContext> _dbContextFactory;
    private readonly Func<TestDbContext, EntityFrameworkCoreStorage<TestDbContext>> _storageFactory;
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();

    private readonly TestableLogger<EntityFrameworkCoreStorage<TestDbContext>> _logger = Substitute.For<
        TestableLogger<EntityFrameworkCoreStorage<TestDbContext>>
    >();

    protected EntityFrameworkCoreDashboardStorageTests(
        Func<TestDbContext> contextFactory,
        EntityFrameworkCoreJobStorageOptions? options = null
    )
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _dbContextFactory = contextFactory;
        _storageFactory = context => new EntityFrameworkCoreStorage<TestDbContext>(
            context,
            options ?? new EntityFrameworkCoreJobStorageOptions(),
            _logger
        );
    }

    // ── GetQueueStatsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetQueueStatsAsync_WhenJobsExistAcrossQueues_ShouldReturnCorrectCountsPerQueue()
    {
        // Arrange
        var now = _clock.UtcNow;
        var defaultQueue = QueueKey.Default;
        var priorityQueue = new QueueKey("priority");

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // default queue: 2 pending, 1 processing (via lease), 1 completed, 1 failed
        var pending1 = AtomizerJob.Create(defaultQueue, typeof(WriteLineMessage), """{"Message":"p1"}""", now, now);
        var pending2 = AtomizerJob.Create(defaultQueue, typeof(WriteLineMessage), """{"Message":"p2"}""", now, now);
        var processing = AtomizerJob.Create(defaultQueue, typeof(WriteLineMessage), """{"Message":"pr"}""", now, now);
        var completed = AtomizerJob.Create(defaultQueue, typeof(WriteLineMessage), """{"Message":"c"}""", now, now);
        var failed = AtomizerJob.Create(defaultQueue, typeof(WriteLineMessage), """{"Message":"f"}""", now, now);

        // priority queue: 1 pending
        var priorityPending = AtomizerJob.Create(
            priorityQueue,
            typeof(WriteLineMessage),
            """{"Message":"pq"}""",
            now,
            now
        );

        await storage.InsertAsync(pending1, CancellationToken.None);
        await storage.InsertAsync(pending2, CancellationToken.None);
        await storage.InsertAsync(processing, CancellationToken.None);
        await storage.InsertAsync(completed, CancellationToken.None);
        await storage.InsertAsync(failed, CancellationToken.None);
        await storage.InsertAsync(priorityPending, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Transition job statuses
        var leaseToken = new LeaseToken($"test-worker:*:{defaultQueue.Key}:*:{Guid.NewGuid():N}");
        processing.Lease(leaseToken, now, TimeSpan.FromMinutes(5));
        completed.MarkAsCompleted(now);
        failed.MarkAsFailed(now);

        await storage.UpdateJobsAsync([processing, completed, failed], CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        // Act
        var stats = await storage.GetQueueStatsAsync(CancellationToken.None);

        // Assert
        stats.Should().HaveCount(2);

        var defaultStats = stats.Single(s => s.QueueKey.Key == defaultQueue.Key);
        defaultStats.Pending.Should().Be(2);
        defaultStats.Processing.Should().Be(1);
        defaultStats.Completed.Should().Be(1);
        defaultStats.Failed.Should().Be(1);
        defaultStats.Total.Should().Be(5);

        var priorityStats = stats.Single(s => s.QueueKey.Key == priorityQueue.Key);
        priorityStats.Pending.Should().Be(1);
        priorityStats.Processing.Should().Be(0);
        priorityStats.Completed.Should().Be(0);
        priorityStats.Failed.Should().Be(0);
        priorityStats.Total.Should().Be(1);
    }

    [Fact]
    public async Task GetQueueStatsAsync_WhenNoJobsExist_ShouldReturnEmptyList()
    {
        // Arrange
        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Act
        var stats = await storage.GetQueueStatsAsync(CancellationToken.None);

        // Assert
        stats.Should().BeEmpty();
    }

    [Fact]
    public async Task GetQueueStatsAsync_WhenResultsReturned_ShouldBeOrderedAlphabeticallyByQueueKey()
    {
        // Arrange
        var now = _clock.UtcNow;

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        var queues = new[] { "zebra", "alpha", "middle" };
        foreach (var name in queues)
        {
            var job = AtomizerJob.Create(new QueueKey(name), typeof(WriteLineMessage), """{"Message":"x"}""", now, now);
            await storage.InsertAsync(job, CancellationToken.None);
        }

        dbContext.ChangeTracker.Clear();

        // Act
        var stats = await storage.GetQueueStatsAsync(CancellationToken.None);

        // Assert
        var keys = stats.Select(s => s.QueueKey.Key).ToList();
        keys.Should().BeInAscendingOrder();
    }

    // ── GetRecentJobsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetRecentJobsAsync_WhenJobsExist_ShouldReturnJobsOrderedByCreatedAtDescending()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow;

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        var oldest = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"oldest"}""",
            baseTime.AddSeconds(-2),
            baseTime.AddSeconds(-2)
        );
        var middle = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"middle"}""",
            baseTime.AddSeconds(-1),
            baseTime.AddSeconds(-1)
        );
        var newest = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"newest"}""",
            baseTime,
            baseTime
        );

        await storage.InsertAsync(oldest, CancellationToken.None);
        await storage.InsertAsync(middle, CancellationToken.None);
        await storage.InsertAsync(newest, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        var jobs = await storage.GetRecentJobsAsync(0, 10, CancellationToken.None);

        // Assert
        jobs.Should().HaveCount(3);
        jobs[0].Id.Should().Be(newest.Id);
        jobs[1].Id.Should().Be(middle.Id);
        jobs[2].Id.Should().Be(oldest.Id);
    }

    [Fact]
    public async Task GetRecentJobsAsync_WhenPaginatingWithSkipAndTake_ShouldReturnCorrectPage()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow;

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Insert 5 jobs ordered oldest → newest
        var insertedIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var job = AtomizerJob.Create(
                QueueKey.Default,
                typeof(WriteLineMessage),
                $"{{\"Message\": \"job{i}\"}}",
                baseTime.AddSeconds(i),
                baseTime.AddSeconds(i)
            );
            await storage.InsertAsync(job, CancellationToken.None);
            insertedIds.Add(job.Id);
        }

        dbContext.ChangeTracker.Clear();

        // Act — GetRecentJobsAsync returns newest first, so skip=2 take=2 gives jobs at index [2,3]
        // from the descending-by-createdAt view: job4, job3, job2, job1, job0
        var page = await storage.GetRecentJobsAsync(2, 2, CancellationToken.None);

        // Assert
        page.Should().HaveCount(2);
        // third and fourth newest == job2 and job1
        page[0].Id.Should().Be(insertedIds[2]); // job2
        page[1].Id.Should().Be(insertedIds[1]); // job1
    }

    [Fact]
    public async Task GetRecentJobsAsync_WhenNoJobsExist_ShouldReturnEmptyList()
    {
        // Arrange
        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Act
        var jobs = await storage.GetRecentJobsAsync(0, 10, CancellationToken.None);

        // Assert
        jobs.Should().BeEmpty();
    }

    // ── GetAllSchedulesAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAllSchedulesAsync_WhenSchedulesExist_ShouldReturnAllSchedulesOrderedByJobKeyAscending()
    {
        // Arrange
        var now = _clock.UtcNow;

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        var scheduleC = AtomizerSchedule.Create(
            new JobKey("C-schedule"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"c"}""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            now
        );
        var scheduleA = AtomizerSchedule.Create(
            new JobKey("A-schedule"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"a"}""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            now
        );
        var scheduleB = AtomizerSchedule.Create(
            new JobKey("B-schedule"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"b"}""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            now
        );

        await storage.UpsertScheduleAsync(scheduleC, CancellationToken.None);
        await storage.UpsertScheduleAsync(scheduleA, CancellationToken.None);
        await storage.UpsertScheduleAsync(scheduleB, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        var schedules = await storage.GetAllSchedulesAsync(CancellationToken.None);

        // Assert
        schedules.Should().HaveCount(3);
        schedules[0].JobKey.Key.Should().Be("A-schedule");
        schedules[1].JobKey.Key.Should().Be("B-schedule");
        schedules[2].JobKey.Key.Should().Be("C-schedule");
    }

    [Fact]
    public async Task GetAllSchedulesAsync_WhenNoSchedulesExist_ShouldReturnEmptyList()
    {
        // Arrange
        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Act
        var schedules = await storage.GetAllSchedulesAsync(CancellationToken.None);

        // Assert
        schedules.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllSchedulesAsync_ShouldReturnBothEnabledAndDisabledSchedules()
    {
        // Arrange
        var now = _clock.UtcNow;

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        var enabledSchedule = AtomizerSchedule.Create(
            new JobKey("enabled-job"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"enabled"}""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            now
        );
        var disabledSchedule = AtomizerSchedule.Create(
            new JobKey("disabled-job"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"disabled"}""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            now
        );
        disabledSchedule.Enabled = false;

        await storage.UpsertScheduleAsync(enabledSchedule, CancellationToken.None);
        await storage.UpsertScheduleAsync(disabledSchedule, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        var schedules = await storage.GetAllSchedulesAsync(CancellationToken.None);

        // Assert — unlike GetDueSchedulesAsync, this must return ALL schedules regardless of Enabled
        schedules.Should().HaveCount(2);
        schedules.Should().Contain(s => s.JobKey.Key == "enabled-job");
        schedules.Should().Contain(s => s.JobKey.Key == "disabled-job");
    }

    // ── GetJobByIdAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetJobByIdAsync_WhenJobExists_ShouldReturnCorrectJob()
    {
        // Arrange
        var now = _clock.UtcNow;

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        var job = AtomizerJob.Create(QueueKey.Default, typeof(WriteLineMessage), """{"Message":"find-me"}""", now, now);

        await storage.InsertAsync(job, CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        // Act
        var found = await storage.GetJobByIdAsync(job.Id, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Id.Should().Be(job.Id);
        found.Payload.Should().Be("""{"Message":"find-me"}""");
        found.Status.Should().Be(AtomizerJobStatus.Pending);
        found.QueueKey.Key.Should().Be(QueueKey.Default.Key);
    }

    [Fact]
    public async Task GetJobByIdAsync_WhenJobExistsWithErrors_ShouldReturnJobIncludingErrorHistory()
    {
        // Arrange
        var now = _clock.UtcNow;

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Build a job that already carries an error record (simulating a failed attempt)
        var job = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"with-error"}""",
            now,
            now
        );
        var error = AtomizerJobError.Create(
            job.Id,
            now,
            attempt: 1,
            exception: new InvalidOperationException("boom"),
            runtimeIdentity: "worker-1"
        );
        job.Errors.Add(error);

        // InsertAsync cascades the error rows through ToEntity() → Errors list
        await storage.InsertAsync(job, CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        // Act
        var found = await storage.GetJobByIdAsync(job.Id, CancellationToken.None);

        // Assert
        found.Should().NotBeNull();
        found!.Errors.Should().HaveCount(1);
        found.Errors[0].ErrorMessage.Should().Be("boom");
        found.Errors[0].ExceptionType.Should().Contain(nameof(InvalidOperationException));
        found.Errors[0].RuntimeIdentity.Should().Be("worker-1");
        found.Errors[0].Attempt.Should().Be(1);
    }

    [Fact]
    public async Task GetJobByIdAsync_WhenJobDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Act
        var result = await storage.GetJobByIdAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    // ── GetLastJobForScheduleAsync ────────────────────────────────────────────

    [Fact]
    public async Task GetLastJobForScheduleAsync_WhenMultipleJobsExist_ShouldReturnMostRecentlyUpdatedJob()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow;
        var jobKey = new JobKey("my-recurring-schedule");

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Insert two jobs produced by the same recurring schedule
        var earlierJob = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"run1"}""",
            baseTime.AddSeconds(-20),
            baseTime.AddSeconds(-20),
            scheduleJobKey: jobKey
        );
        var laterJob = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"run2"}""",
            baseTime.AddSeconds(-10),
            baseTime.AddSeconds(-10),
            scheduleJobKey: jobKey
        );

        await storage.InsertAsync(earlierJob, CancellationToken.None);
        await storage.InsertAsync(laterJob, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Advance the later job's UpdatedAt by completing it after the earlier job
        earlierJob.MarkAsCompleted(baseTime.AddSeconds(-15));
        laterJob.MarkAsCompleted(baseTime.AddSeconds(-5));

        await storage.UpdateJobsAsync([earlierJob, laterJob], CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        // Act
        var lastJob = await storage.GetLastJobForScheduleAsync(jobKey, CancellationToken.None);

        // Assert
        lastJob.Should().NotBeNull();
        lastJob!.Id.Should().Be(laterJob.Id);
    }

    [Fact]
    public async Task GetLastJobForScheduleAsync_WhenJobsExistForDifferentSchedules_ShouldOnlyReturnJobForRequestedSchedule()
    {
        // Arrange
        var now = _clock.UtcNow;
        var targetKey = new JobKey("target-schedule");
        var otherKey = new JobKey("other-schedule");

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        var targetJob = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"target"}""",
            now,
            now,
            scheduleJobKey: targetKey
        );
        var otherJob = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{"Message":"other"}""",
            now,
            now,
            scheduleJobKey: otherKey
        );

        await storage.InsertAsync(targetJob, CancellationToken.None);
        await storage.InsertAsync(otherJob, CancellationToken.None);
        dbContext.ChangeTracker.Clear();

        // Act
        var result = await storage.GetLastJobForScheduleAsync(targetKey, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(targetJob.Id);
    }

    [Fact]
    public async Task GetLastJobForScheduleAsync_WhenNoJobsExistForSchedule_ShouldReturnNull()
    {
        // Arrange
        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Act
        var result = await storage.GetLastJobForScheduleAsync(
            new JobKey("nonexistent-schedule"),
            CancellationToken.None
        );

        // Assert
        result.Should().BeNull();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await using var dbContext = _dbContextFactory();
        dbContext.Set<AtomizerJobEntity>().RemoveRange(dbContext.Set<AtomizerJobEntity>());
        dbContext.Set<AtomizerJobErrorEntity>().RemoveRange(dbContext.Set<AtomizerJobErrorEntity>());
        dbContext.Set<AtomizerScheduleEntity>().RemoveRange(dbContext.Set<AtomizerScheduleEntity>());
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;
}

// ── Provider executors ────────────────────────────────────────────────────────

[Collection(nameof(PostgreSqlDatabaseFixture))]
public class PostgreSqlDashboardStorageTestsExecutor(PostgreSqlDatabaseFixture fixture)
    : EntityFrameworkCoreDashboardStorageTests(fixture.CreateNewDbContext);

[Collection(nameof(MySqlDatabaseFixture))]
public class MySqlDashboardStorageTestsExecutor(MySqlDatabaseFixture fixture)
    : EntityFrameworkCoreDashboardStorageTests(fixture.CreateNewDbContext);

[Collection(nameof(SqlServerDatabaseFixture))]
public class SqlServerDashboardStorageTestsExecutor(SqlServerDatabaseFixture fixture)
    : EntityFrameworkCoreDashboardStorageTests(fixture.CreateNewDbContext);

[Collection(nameof(SqliteDatabaseFixture))]
public class SqliteDashboardStorageTestsExecutor(SqliteDatabaseFixture fixture)
    : EntityFrameworkCoreDashboardStorageTests(
        fixture.CreateNewDbContext,
        new EntityFrameworkCoreJobStorageOptions { AllowUnsafeProviderFallback = true }
    );
