using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using DotNet.Testcontainers.Containers;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

public abstract class BaseDatabaseFixture : IAsyncLifetime
{
    protected readonly IDatabaseContainer DatabaseContainer;

    public TestDbContext DbContext { get; protected set; } = null!;

    protected BaseDatabaseFixture(IDatabaseContainer databaseContainer)
    {
        DatabaseContainer = databaseContainer;
    }

    public async ValueTask InitializeAsync()
    {
        await DatabaseContainer.StartAsync();
        DbContext = await InitializeDbContext();
    }

    protected abstract Task<TestDbContext> InitializeDbContext();

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await DatabaseContainer.StopAsync();
        await DatabaseContainer.DisposeAsync();
    }
}
