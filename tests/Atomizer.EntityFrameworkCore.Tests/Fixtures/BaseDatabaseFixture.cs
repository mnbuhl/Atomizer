using Atomizer.EntityFrameworkCore.Tests.TestSetup;
using Atomizer.EntityFrameworkCore.Tests.TestSetup.Postgres;
using DotNet.Testcontainers.Containers;

namespace Atomizer.EntityFrameworkCore.Tests.Fixtures;

public abstract class BaseDatabaseFixture : IAsyncLifetime
{
    protected readonly IDatabaseContainer DatabaseContainer;

    public PostgresDbContext DbContext { get; protected set; } = null!;

    protected BaseDatabaseFixture(IDatabaseContainer databaseContainer)
    {
        DatabaseContainer = databaseContainer;
    }

    public async ValueTask InitializeAsync()
    {
        await DatabaseContainer.StartAsync();
        DbContext = await InitializeDbContext();
    }

    protected abstract Task<PostgresDbContext> InitializeDbContext();

    public async ValueTask DisposeAsync()
    {
        await DbContext.DisposeAsync();
        await DatabaseContainer.StopAsync();
        await DatabaseContainer.DisposeAsync();
    }
}
