using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using Atomizer.Tests.Utilities;
using Atomizer.Tests.Utilities.Stubs;
using Atomizer.Tests.Utilities.TestJobs;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage;

public abstract class EntityFrameworkCoreStorageTests : IAsyncLifetime
{
    private readonly Func<TestDbContext> _dbContextFactory;
    private readonly Func<TestDbContext, EntityFrameworkCoreStorage<TestDbContext>> _storageFactory;
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();

    private readonly TestableLogger<EntityFrameworkCoreStorage<TestDbContext>> _logger = Substitute.For<
        TestableLogger<EntityFrameworkCoreStorage<TestDbContext>>
    >();

    protected EntityFrameworkCoreStorageTests(
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

    [Fact]
    public async Task InsertAsync_WhenValidJob_ShouldInsertJob()
    {
        // Arrange
        var now = _clock.UtcNow;
        var job = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Hello, World!" }""",
            now,
            now
        );

        // Act
        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);
        var jobId = await storage.InsertAsync(job, CancellationToken.None);

        // Assert
        jobId.Should().NotBeEmpty();
        jobId.Should().Be(job.Id);
    }

    [Fact]
    public async Task InsertAsync_WhenJobWithIdempotencyKeyExists_ShouldNotInsertDuplicateJob()
    {
        // Arrange
        var now = _clock.UtcNow;
        var idempotencyKey = "unique-key";
        var job1 = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "First Job" }""",
            now,
            now,
            idempotencyKey: idempotencyKey
        );
        var job2 = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Second Job" }""",
            now,
            now,
            idempotencyKey: idempotencyKey
        );

        // Act
        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);
        var jobId1 = await storage.InsertAsync(job1, CancellationToken.None);
        var jobId2 = await storage.InsertAsync(job2, CancellationToken.None);
        var jobsInDb = await dbContext.Set<AtomizerJobEntity>().ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        jobId1.Should().Be(job1.Id);
        jobId2.Should().Be(jobId1); // Should return the same ID as the first job
        jobsInDb.Should().HaveCount(1); // Only one job should be
        jobsInDb[0].Payload.Should().Be("""{ "message": "First Job" }""");

        var map = () => jobsInDb[0].ToAtomizerJob();
        map.Should().NotThrow();
    }

    [Fact]
    public async Task UpdateJobsAsync_WhenJobsExist_ShouldUpdateJobs()
    {
        // Arrange
        var now = _clock.UtcNow;
        var job1 = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Job 1" }""",
            now,
            now
        );
        var job2 = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Job 2" }""",
            now,
            now
        );
        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);
        await storage.InsertAsync(job1, CancellationToken.None);
        await storage.InsertAsync(job2, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        job1.MarkAsCompleted(_clock.UtcNow);
        job2.MarkAsFailed(_clock.UtcNow);
        await storage.UpdateJobsAsync(new[] { job1, job2 }, CancellationToken.None);
        var updatedJobEntities = await dbContext
            .Set<AtomizerJobEntity>()
            .Where(j => j.Id == job1.Id || j.Id == job2.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        updatedJobEntities.Should().HaveCount(2);

        var updatedJob1 = updatedJobEntities.First(j => j.Id == job1.Id);
        updatedJob1.Status.Should().Be(AtomizerEntityJobStatus.Completed);

        var updatedJob2 = updatedJobEntities.First(j => j.Id == job2.Id);
        updatedJob2.Status.Should().Be(AtomizerEntityJobStatus.Failed);

        var map1 = () => updatedJob1.ToAtomizerJob();
        map1.Should().NotThrow();

        var map2 = () => updatedJob2.ToAtomizerJob();
        map2.Should().NotThrow();
    }

    [Fact]
    public async Task GetDueJobsAsync_WhenDueJobsExist_ShouldReturnDueJobs()
    {
        // Arrange
        var now = _clock.UtcNow;
        var pastTime = now.AddMinutes(-10);
        var futureTime = now.AddMinutes(10);

        var dueJob1 = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Due Job 1" }""",
            pastTime,
            pastTime
        );
        var dueJob2 = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Due Job 2" }""",
            pastTime,
            pastTime
        );
        var notDueJob = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Not Due Job" }""",
            futureTime,
            futureTime
        );

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);
        await storage.InsertAsync(dueJob1, CancellationToken.None);
        await storage.InsertAsync(dueJob2, CancellationToken.None);
        await storage.InsertAsync(notDueJob, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        var dueJobs = await storage.GetDueJobsAsync(QueueKey.Default, now, 10, CancellationToken.None);

        // Assert
        dueJobs.Should().HaveCount(2);
        dueJobs.Should().Contain(j => j.Id == dueJob1.Id);
        dueJobs.Should().Contain(j => j.Id == dueJob2.Id);
        dueJobs.Should().NotContain(j => j.Id == notDueJob.Id);
    }

    [Fact]
    public async Task GetDueJobsAsync_WhenForUpdate_ShouldNotReturnSameJobs()
    {
        // Arrange
        var now = _clock.UtcNow;
        var pastTime = now.AddMinutes(-10);

        var dueJob1 = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Due Job 1" }""",
            pastTime,
            pastTime
        );
        var dueJob2 = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Due Job 2" }""",
            pastTime,
            pastTime
        );

        await using var dbContext1 = _dbContextFactory();
        var storage1 = _storageFactory(dbContext1);

        if (dbContext1.Database.IsSqlite())
        {
            // SQLite does not support "FOR UPDATE SKIP LOCKED" behavior, so we skip this test for SQLite.
            return;
        }

        await using var dbContext2 = _dbContextFactory();
        var storage2 = _storageFactory(dbContext2);

        await storage1.InsertAsync(dueJob1, CancellationToken.None);
        await storage2.InsertAsync(dueJob2, CancellationToken.None);

        dbContext1.ChangeTracker.Clear();

        await using var transaction1 = await dbContext1.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );

        await using var transaction2 = await dbContext2.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );

        // Act
        var dueJobsFirstFetch = await storage1.GetDueJobsAsync(QueueKey.Default, now, 10, CancellationToken.None);
        var dueJobsSecondFetch = await storage2.GetDueJobsAsync(QueueKey.Default, now, 10, CancellationToken.None);

        await transaction1.RollbackAsync(TestContext.Current.CancellationToken);
        await transaction2.RollbackAsync(TestContext.Current.CancellationToken);

        // Assert
        dueJobsFirstFetch.Should().HaveCount(2);
        dueJobsSecondFetch.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueJobsAsync_WhenNoDueJobsExist_ShouldReturnEmptyList()
    {
        // Arrange
        var now = _clock.UtcNow;
        var futureTime = now.AddMinutes(10);

        var notDueJob = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Not Due Job" }""",
            futureTime,
            futureTime
        );

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        await storage.InsertAsync(notDueJob, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        var dueJobs = await storage.GetDueJobsAsync(QueueKey.Default, now, 10, CancellationToken.None);

        // Assert
        dueJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task ReleaseLeasedJobsAsync_WhenLeasedJobsExist_ShouldReleaseJobs()
    {
        // Arrange
        var now = _clock.UtcNow;
        var job = AtomizerJob.Create(
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Leased Job" }""",
            now,
            now
        );

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        await storage.InsertAsync(job, CancellationToken.None);

        var leaseToken = FakeDataFactory.LeaseToken();

        dbContext.ChangeTracker.Clear();

        // Simulate leasing the job
        job.Lease(leaseToken, _clock.UtcNow, TimeSpan.FromMinutes(2));
        await storage.UpdateJobsAsync([job], CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        await storage.ReleaseLeasedAsync(leaseToken, _clock.UtcNow, CancellationToken.None);
        var releasedJobEntity = await dbContext
            .Set<AtomizerJobEntity>()
            .FirstOrDefaultAsync(j => j.Id == job.Id, TestContext.Current.CancellationToken);

        // Assert
        releasedJobEntity.Should().NotBeNull();
        releasedJobEntity.Status.Should().Be(AtomizerEntityJobStatus.Pending);
        releasedJobEntity.LeaseToken.Should().BeNull();
        releasedJobEntity.VisibleAt.Should().BeNull();
    }

    [Fact]
    public async Task ReleaseLeasedJobsAsync_WhenNoLeasedJobsExist_ShouldNotThrow()
    {
        // Arrange
        var leaseToken = FakeDataFactory.LeaseToken();

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Act
        var act = async () => await storage.ReleaseLeasedAsync(leaseToken, _clock.UtcNow, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetDueSchedulesAsync_WhenDueSchedulesExist_ShouldReturnDueSchedules()
    {
        // Arrange
        var now = _clock.UtcNow;
        var pastTime = now.AddMinutes(-10);
        var futureTime = now.AddMinutes(10);

        var dueSchedule1 = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-1"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Due Schedule 1" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            pastTime
        );
        var dueSchedule2 = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-2"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Due Schedule 2" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            pastTime
        );
        var notDueSchedule = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-3"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Not Due Schedule" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            futureTime
        );

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        await storage.UpsertScheduleAsync(dueSchedule1, CancellationToken.None);
        await storage.UpsertScheduleAsync(dueSchedule2, CancellationToken.None);
        await storage.UpsertScheduleAsync(notDueSchedule, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        var dueSchedules = await storage.GetDueSchedulesAsync(now.AddMinutes(1), CancellationToken.None);

        // Assert
        dueSchedules.Should().HaveCount(2);
        dueSchedules.Should().Contain(s => s.Id == dueSchedule1.Id);
        dueSchedules.Should().Contain(s => s.Id == dueSchedule2.Id);
        dueSchedules.Should().NotContain(s => s.Id == notDueSchedule.Id);
    }

    [Fact]
    public async Task GetDueSchedulesAsync_WhenNoDueSchedulesExist_ShouldReturnEmptyList()
    {
        // Arrange
        var now = _clock.UtcNow;
        var futureTime = now.AddMinutes(10);

        var notDueSchedule = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-1"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Not Due Schedule" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            futureTime
        );

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        await storage.UpsertScheduleAsync(notDueSchedule, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        var dueSchedules = await storage.GetDueSchedulesAsync(now, CancellationToken.None);

        // Assert
        dueSchedules.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueSchedules_WhenForUpdate_ShouldNotReturnSameSchedules()
    {
        // Arrange
        var now = _clock.UtcNow;
        var pastTime = now.AddMinutes(-10);

        var dueSchedule1 = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-1"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Due Schedule 1" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            pastTime
        );
        var dueSchedule2 = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-2"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Due Schedule 2" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            pastTime
        );

        await using var dbContext1 = _dbContextFactory();
        var storage1 = _storageFactory(dbContext1);

        if (dbContext1.Database.IsSqlite())
        {
            // SQLite does not support "FOR UPDATE SKIP LOCKED" behavior, so we skip this test for SQLite.
            return;
        }

        await using var dbContext2 = _dbContextFactory();
        var storage2 = _storageFactory(dbContext2);

        await storage1.UpsertScheduleAsync(dueSchedule1, CancellationToken.None);
        await storage2.UpsertScheduleAsync(dueSchedule2, CancellationToken.None);

        dbContext1.ChangeTracker.Clear();

        await using var transaction1 = await dbContext1.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );

        await using var transaction2 = await dbContext2.Database.BeginTransactionAsync(
            TestContext.Current.CancellationToken
        );

        // Act
        var dueSchedulesFirstFetch = await storage1.GetDueSchedulesAsync(now, CancellationToken.None);
        var dueSchedulesSecondFetch = await storage2.GetDueSchedulesAsync(now, CancellationToken.None);

        await transaction1.RollbackAsync(TestContext.Current.CancellationToken);
        await transaction2.RollbackAsync(TestContext.Current.CancellationToken);

        // Assert
        dueSchedulesFirstFetch.Should().HaveCount(2);
        dueSchedulesSecondFetch.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateSchedulesAsync_WhenSchedulesExist_ShouldUpdateSchedules()
    {
        // Arrange
        var now = _clock.UtcNow;
        var schedule1 = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-1"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Schedule 1" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            now
        );
        var schedule2 = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-2"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Schedule 2" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            now
        );

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        await storage.UpsertScheduleAsync(schedule1, CancellationToken.None);
        await storage.UpsertScheduleAsync(schedule2, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        schedule1.Enabled = false;
        schedule2.MaxCatchUp = 10;
        await storage.UpdateSchedulesAsync(new[] { schedule1, schedule2 }, CancellationToken.None);
        var updatedScheduleEntities = await dbContext
            .Set<AtomizerScheduleEntity>()
            .Where(s => s.Id == schedule1.Id || s.Id == schedule2.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        updatedScheduleEntities.Should().HaveCount(2);

        var updatedSchedule1 = updatedScheduleEntities.First(s => s.Id == schedule1.Id);
        updatedSchedule1.Enabled.Should().BeFalse();

        var updatedSchedule2 = updatedScheduleEntities.First(s => s.Id == schedule2.Id);
        updatedSchedule2.MaxCatchUp.Should().Be(10);

        var map1 = () => updatedSchedule1.ToAtomizerSchedule();
        map1.Should().NotThrow();

        var map2 = () => updatedSchedule2.ToAtomizerSchedule();
        map2.Should().NotThrow();
    }

    [Fact]
    public async Task UpsertScheduleAsync_WhenScheduleDoesNotExist_ShouldInsertSchedule()
    {
        // Arrange
        var now = _clock.UtcNow;
        var schedule = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-1"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "New Schedule" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            now
        );

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        // Act
        var scheduleId = await storage.UpsertScheduleAsync(schedule, CancellationToken.None);
        var insertedScheduleEntity = await dbContext
            .Set<AtomizerScheduleEntity>()
            .FirstOrDefaultAsync(s => s.Id == scheduleId, TestContext.Current.CancellationToken);

        // Assert
        scheduleId.Should().Be(schedule.Id);
        insertedScheduleEntity.Should().NotBeNull();
        insertedScheduleEntity.JobKey.Should().Be(schedule.JobKey);
        insertedScheduleEntity.Payload.Should().Be(schedule.Payload);

        var map = () => insertedScheduleEntity.ToAtomizerSchedule();
        map.Should().NotThrow();
    }

    [Fact]
    public async Task UpsertScheduleAsync_WhenScheduleExists_ShouldUpdateSchedule()
    {
        // Arrange
        var now = _clock.UtcNow;
        var schedule = AtomizerSchedule.Create(
            new JobKey("WriteLineMessage-1"),
            QueueKey.Default,
            typeof(WriteLineMessage),
            """{ "message": "Initial Schedule" }""",
            Schedule.EveryMinute,
            TimeZoneInfo.Utc,
            now
        );

        await using var dbContext = _dbContextFactory();
        var storage = _storageFactory(dbContext);

        await storage.UpsertScheduleAsync(schedule, CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        // Act
        schedule.Payload = """{ "message": "Updated Schedule" }""";
        var scheduleId = await storage.UpsertScheduleAsync(schedule, CancellationToken.None);
        var updatedScheduleEntity = await dbContext
            .Set<AtomizerScheduleEntity>()
            .FirstOrDefaultAsync(s => s.Id == scheduleId, TestContext.Current.CancellationToken);

        // Assert
        scheduleId.Should().Be(schedule.Id);
        updatedScheduleEntity.Should().NotBeNull();
        updatedScheduleEntity.Payload.Should().Be("""{ "message": "Updated Schedule" }""");

        var map = () => updatedScheduleEntity.ToAtomizerSchedule();
        map.Should().NotThrow();
    }

    public async ValueTask DisposeAsync()
    {
        await using var dbContext = _dbContextFactory();
        dbContext.Set<AtomizerJobEntity>().RemoveRange(dbContext.Set<AtomizerJobEntity>());
        dbContext.Set<AtomizerJobErrorEntity>().RemoveRange(dbContext.Set<AtomizerJobErrorEntity>());
        dbContext.Set<AtomizerScheduleEntity>().RemoveRange(dbContext.Set<AtomizerScheduleEntity>());
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

// ---- Executors per provider (fixtures provide the concrete DbContext) ----

[Collection(nameof(PostgreSqlDatabaseFixture))]
public class PostgreSqlStorageTestsExecutor(PostgreSqlDatabaseFixture fixture)
    : EntityFrameworkCoreStorageTests(fixture.CreateNewDbContext);

[Collection(nameof(MySqlDatabaseFixture))]
public class MySqlStorageTestsExecutor(MySqlDatabaseFixture fixture)
    : EntityFrameworkCoreStorageTests(fixture.CreateNewDbContext);

[Collection(nameof(SqlServerDatabaseFixture))]
public class SqlServerStorageTestsExecutor(SqlServerDatabaseFixture fixture)
    : EntityFrameworkCoreStorageTests(fixture.CreateNewDbContext);

[Collection(nameof(SqliteDatabaseFixture))]
public class SqliteStorageTestsExecutor(SqliteDatabaseFixture fixture)
    : EntityFrameworkCoreStorageTests(
        fixture.CreateNewDbContext,
        new EntityFrameworkCoreJobStorageOptions { AllowUnsafeProviderFallback = true }
    );

// [Collection(nameof(OracleDatabaseFixture))]
// public class OracleStorageTestsExecutor(OracleDatabaseFixture fixture)
//     : EntityFrameworkCoreStorageTests(fixture.CreateNewDbContext);
