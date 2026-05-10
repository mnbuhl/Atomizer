using Atomizer.EntityFrameworkCore.Tests.Fixtures;
using Atomizer.EntityFrameworkCore.Tests.Storage;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.SqlServer;
using AwesomeAssertions;

namespace Atomizer.EntityFrameworkCore.Tests.Dashboard;

[Collection(nameof(SqlServerDatabaseFixture))]
public sealed class SqlServerDashboardStorageTests(SqlServerDatabaseFixture fixture)
    : EntityFrameworkCoreDashboardStorageTests<SqlServerDbContext>
{
    protected override SqlServerDbContext CreateDbContext() => fixture.CreateNewDbContext();

    public override ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public override async ValueTask DisposeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var db = CreateDbContext();
        await StorageTestCleanup.ClearAsync(db, cts.Token);
    }

    [Fact]
    public async Task GetJobsAsync_WhenUsingMsSql_ShouldReturnPagedResults()
    {
        var result = await CreateStorage().GetJobsAsync(new JobQuery { Skip = 0, Take = 10 }, CancellationToken.None);

        result.Should().NotBeNull();
        result.Items.Should().NotBeNull();
    }
}
