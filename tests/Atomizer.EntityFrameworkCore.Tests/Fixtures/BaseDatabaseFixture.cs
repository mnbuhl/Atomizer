using Atomizer.EntityFrameworkCore.Providers;
using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;

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
        DbContext = ConfigureDbContext();

        await DbContext.Database.MigrateAsync();

        RelationalProviderCache.ResetInstanceForTests();
    }

    protected abstract TDbContext ConfigureDbContext();

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await DatabaseContainer.StopAsync();
        await DatabaseContainer.DisposeAsync();
    }
}
