using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using Atomizer.Tests.Utilities;
using AwesomeAssertions;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage;

public abstract class EntityFrameworkCoreStorageTests
{
    private readonly EntityFrameworkCoreStorage<TestDbContext> _storage;
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();

    protected EntityFrameworkCoreStorageTests(TestDbContext storage)
    {
        _clock.UtcNow.Returns(DateTimeOffset.UtcNow);
        _storage = new EntityFrameworkCoreStorage<TestDbContext>(
            storage,
            new EntityFrameworkCoreJobStorageOptions(),
            _clock,
            Substitute.For<TestableLogger<EntityFrameworkCoreStorage<TestDbContext>>>()
        );
    }

    [Fact]
    public async Task InsertAsync_WhenValidJob_ShouldInsertJob()
    {
        // Arrange
        var now = _clock.UtcNow;
        var job = AtomizerJob.Create(QueueKey.Default, typeof(string), "payload", now, now);

        // Act
        var jobId = await _storage.InsertAsync(job, CancellationToken.None);

        // Assert
        jobId.Should().NotBeEmpty();
    }
}

[Collection(nameof(PostgreSqlDatabaseFixture))]
public class PostgreSqlStorageTestsExecutor : EntityFrameworkCoreStorageTests
{
    public PostgreSqlStorageTestsExecutor(PostgreSqlDatabaseFixture fixture)
        : base(fixture.DbContext) { }
}
