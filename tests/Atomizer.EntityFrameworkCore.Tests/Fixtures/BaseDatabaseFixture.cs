using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using DotNet.Testcontainers.Containers;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

public abstract class BaseDatabaseFixture<TDbContext> : IAsyncLifetime
    where TDbContext : TestDbContext
{
    protected readonly IDatabaseContainer DatabaseContainer;

    public TDbContext DbContext { get; protected set; } = null!;

    protected BaseDatabaseFixture(IDatabaseContainer databaseContainer)
    {
        DatabaseContainer = databaseContainer;
    }

    public async ValueTask InitializeAsync()
    {
        await DatabaseContainer.StartAsync();
        DbContext = await InitializeDbContext();
    }

    protected abstract Task<TDbContext> InitializeDbContext();

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await DatabaseContainer.StopAsync();
        await DatabaseContainer.DisposeAsync();
    }
}
