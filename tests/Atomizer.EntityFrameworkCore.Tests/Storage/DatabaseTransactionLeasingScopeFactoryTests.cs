using Atomizer.Abstractions;
using Atomizer.EntityFrameworkCore.Storage;
using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using Atomizer.Tests.Utilities;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Storage;

public abstract class DatabaseTransactionLeasingScopeFactoryTests
{
    private readonly Func<TestDbContext> _dbContextFactory;
    private readonly TestableLogger<DatabaseTransactionLeasingScopeFactory<TestDbContext>> _logger;

    protected DatabaseTransactionLeasingScopeFactoryTests(Func<TestDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
        _logger = Substitute.For<TestableLogger<DatabaseTransactionLeasingScopeFactory<TestDbContext>>>();
    }

    private static QueueKey NewKey() => new QueueKey($"q-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateScopeAsync_WhenRelationalProvider_ShouldAcquireTrue()
    {
        // Arrange
        await using var context = _dbContextFactory();
        var sut = new DatabaseTransactionLeasingScopeFactory<TestDbContext>(context, _logger);

        // Act
        await using var scope = await sut.CreateScopeAsync(NewKey(), TimeSpan.FromSeconds(2), CancellationToken.None);

        // Assert
        scope.Acquired.Should().BeTrue();
        _logger
            .Received()
            .LogDebug(Arg.Is<string>(m => m.Contains("Starting database transaction leasing scope for queue")));
    }

    [Fact]
    public async Task CreateScopeAsync_WhenMultipleConcurrentRequests_ShouldAllowMultipleAcquisitions()
    {
        // Arrange
        await using var context1 = _dbContextFactory();

        if (context1.Database.IsSqlite())
        {
            // SQLite databases do not support multiple concurrent writers through EF Core.
            return;
        }

        await using var context2 = _dbContextFactory();
        await using var context3 = _dbContextFactory();
        var sut1 = new DatabaseTransactionLeasingScopeFactory<TestDbContext>(context1, _logger);
        var sut2 = new DatabaseTransactionLeasingScopeFactory<TestDbContext>(context2, _logger);
        var sut3 = new DatabaseTransactionLeasingScopeFactory<TestDbContext>(context3, _logger);
        var key = NewKey();

        // Act
        var scopes = new List<IAtomizerLeasingScope>();

        await using var scope1 = await sut1.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);
        await using var scope2 = await sut2.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);
        await using var scope3 = await sut3.CreateScopeAsync(key, TimeSpan.FromSeconds(5), CancellationToken.None);
        scopes.AddRange([scope1, scope2, scope3]);

        // Assert
        scopes
            .Should()
            .OnlyContain(
                s => s.Acquired,
                "database transactions are independent and should not block each other in this leasing model"
            );
    }

    [Fact]
    public async Task CreateScopeAsync_WhenCancellationAlreadyRequested_ShouldReturnAcquiredFalse()
    {
        // Arrange
        await using var context = _dbContextFactory();
        var sut = new DatabaseTransactionLeasingScopeFactory<TestDbContext>(context, _logger);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var scope = await sut.CreateScopeAsync(NewKey(), TimeSpan.FromSeconds(5), cts.Token);

        // Assert
        scope.Acquired.Should().BeFalse("factory converts BeginTransactionAsync failures into a non-acquired scope");
        await scope.DisposeAsync(); // no-op; should not throw
    }
}

// ---- Executors per provider (fixtures provide the concrete DbContext) ----

[Collection(nameof(PostgreSqlDatabaseFixture))]
public sealed class DatabaseTransactionLeasingScopeFactoryPostgreSqlExecutor(PostgreSqlDatabaseFixture fixture)
    : DatabaseTransactionLeasingScopeFactoryTests(fixture.CreateNewDbContext);

[Collection(nameof(MySqlDatabaseFixture))]
public sealed class DatabaseTransactionLeasingScopeFactoryMySqlExecutor(MySqlDatabaseFixture fixture)
    : DatabaseTransactionLeasingScopeFactoryTests(fixture.CreateNewDbContext);

[Collection(nameof(SqlServerDatabaseFixture))]
public sealed class DatabaseTransactionLeasingScopeFactorySqlServerExecutor(SqlServerDatabaseFixture fixture)
    : DatabaseTransactionLeasingScopeFactoryTests(fixture.CreateNewDbContext);

[Collection(nameof(SqliteDatabaseFixture))]
public sealed class DatabaseTransactionLeasingScopeFactorySqliteExecutor(SqliteDatabaseFixture fixture)
    : DatabaseTransactionLeasingScopeFactoryTests(fixture.CreateNewDbContext);

// [Collection(nameof(OracleDatabaseFixture))]
// public sealed class DatabaseTransactionLeasingScopeFactoryOracleExecutor(OracleDatabaseFixture fixture)
//     : DatabaseTransactionLeasingScopeFactoryTests(fixture.CreateNewDbContext);
