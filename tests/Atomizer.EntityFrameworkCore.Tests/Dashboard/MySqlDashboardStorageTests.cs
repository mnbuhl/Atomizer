using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.Storage;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.MySql;
using AwesomeAssertions;
using NSubstitute;

namespace Atomizer.EntityFrameworkCore.Tests.Dashboard;

[Collection(nameof(MySqlDatabaseFixture))]
public sealed class MySqlDashboardStorageTests(MySqlDatabaseFixture fixture)
    : EntityFrameworkCoreDashboardStorageTests<MySqlDbContext>
{
    protected override MySqlDbContext CreateDbContext() => fixture.CreateNewDbContext();

    public override ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public override async ValueTask DisposeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var db = CreateDbContext();
        await StorageTestCleanup.ClearAsync(db, cts.Token);
    }

    [Fact]
    public async Task GetJobsAsync_WhenUsingMySql_ShouldReturnPagedResults()
    {
        var now = DateTimeOffset.UtcNow;
        Clock.UtcNow.Returns(now);

        var result = await CreateStorage().GetJobsAsync(new JobQuery { Skip = 0, Take = 10 }, CancellationToken.None);

        result.Should().NotBeNull();
        result.Items.Should().NotBeNull();
    }
}
