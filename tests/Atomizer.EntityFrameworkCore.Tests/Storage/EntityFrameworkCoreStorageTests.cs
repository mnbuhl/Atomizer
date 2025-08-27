using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using Atomizer.Tests.Utilities;
using Atomizer.Tests.Utilities.TestJobs;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage;

public abstract class EntityFrameworkCoreStorageTests : IAsyncLifetime
{
    private readonly EntityFrameworkCoreStorage<TestDbContext> _storage;
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly TestDbContext _dbContext;
    private readonly EntityFrameworkCoreJobStorageOptions _options = new EntityFrameworkCoreJobStorageOptions();
    private readonly TestableLogger<EntityFrameworkCoreStorage<TestDbContext>> _logger = Substitute.For<
        TestableLogger<EntityFrameworkCoreStorage<TestDbContext>>
    >();

    protected EntityFrameworkCoreStorageTests(TestDbContext storage)
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _dbContext = storage;
        _storage = new EntityFrameworkCoreStorage<TestDbContext>(storage, _options, _clock, _logger);
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
        var jobId = await _storage.InsertAsync(job, CancellationToken.None);

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
        var jobId1 = await _storage.InsertAsync(job1, CancellationToken.None);
        var jobId2 = await _storage.InsertAsync(job2, CancellationToken.None);
        var jobsInDb = await _dbContext.Set<AtomizerJobEntity>().ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        jobId1.Should().Be(job1.Id);
        jobId2.Should().Be(jobId1); // Should return the same ID as the first job
        jobsInDb.Should().HaveCount(1); // Only one job should be
        jobsInDb[0].Payload.Should().Be("""{ "message": "First Job" }""");

        var map = () => jobsInDb[0].ToAtomizerJob();
        map.Should().NotThrow();
    }

    [Fact]
    public async Task UpdateJobAsync_WhenJobExists_ShouldUpdateJob()
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
        await _storage.InsertAsync(job, CancellationToken.None);

        // Act
        job.MarkAsCompleted(_clock.UtcNow);
        await _storage.UpdateJobAsync(job, CancellationToken.None);
        var updatedJobEntity = await _dbContext
            .Set<AtomizerJobEntity>()
            .FirstOrDefaultAsync(j => j.Id == job.Id, TestContext.Current.CancellationToken);

        // Assert
        updatedJobEntity.Should().NotBeNull();
        updatedJobEntity.Status.Should().Be(AtomizerEntityJobStatus.Completed);

        var map = () => updatedJobEntity.ToAtomizerJob();
        map.Should().NotThrow();
    }

    [Fact]
    public async Task UpdateJobAsync_WhenJobDoesNotExist_ShouldLogErrorAndContinue()
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
        await _storage.UpdateJobAsync(job, CancellationToken.None);

        // Assert
        _logger.Received(1).LogError(Arg.Any<DbUpdateException>(), $"Failed to update job {job.Id}");
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
        await _storage.InsertAsync(job1, CancellationToken.None);
        await _storage.InsertAsync(job2, CancellationToken.None);

        _dbContext.ChangeTracker.Clear();

        // Act
        job1.MarkAsCompleted(_clock.UtcNow);
        job2.MarkAsFailed(_clock.UtcNow);
        await _storage.UpdateJobsAsync(new[] { job1, job2 }, CancellationToken.None);
        var updatedJobEntities = await _dbContext
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

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async ValueTask InitializeAsync()
    {
        _dbContext.RemoveRange(_dbContext.Set<AtomizerJobEntity>());
        _dbContext.RemoveRange(_dbContext.Set<AtomizerJobErrorEntity>());
        _dbContext.RemoveRange(_dbContext.Set<AtomizerScheduleEntity>());
        await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        _dbContext.ChangeTracker.Clear();
    }
}

[Collection(nameof(PostgreSqlDatabaseFixture))]
public class PostgreSqlStorageTestsExecutor : EntityFrameworkCoreStorageTests
{
    public PostgreSqlStorageTestsExecutor(PostgreSqlDatabaseFixture fixture)
        : base(fixture.DbContext) { }
}
