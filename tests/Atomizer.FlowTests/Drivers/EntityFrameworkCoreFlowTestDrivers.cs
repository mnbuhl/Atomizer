using Atomizer.EntityFrameworkCore;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.FlowTests.Infrastructure;
using DotNet.Testcontainers.Containers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.PostgreSql;

namespace Atomizer.FlowTests.Drivers;

public abstract class EntityFrameworkCoreFlowTestDriver : FlowTestDriver
{
    private readonly IDatabaseContainer _container;
    private readonly string? _schema;

    protected EntityFrameworkCoreFlowTestDriver(IDatabaseContainer container, string? schema)
    {
        _container = container;
        _schema = schema;
    }

    public override async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public override void ConfigureServices(IServiceCollection services, string runId)
    {
        services.AddScoped(_ => CreateDbContext());
    }

    public override void ConfigureStorage(AtomizerOptions options, string runId)
    {
        options.UseEntityFrameworkCoreStorage<FlowTestDbContext>();
    }

    public override async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = CreateDbContext();

        dbContext.Set<AtomizerJobErrorEntity>().RemoveRange(dbContext.Set<AtomizerJobErrorEntity>());
        dbContext.Set<AtomizerJobEntity>().RemoveRange(dbContext.Set<AtomizerJobEntity>());
        dbContext.Set<AtomizerScheduleEntity>().RemoveRange(dbContext.Set<AtomizerScheduleEntity>());
        dbContext.Set<AtomizerActiveServerEntity>().RemoveRange(dbContext.Set<AtomizerActiveServerEntity>());

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    protected abstract void ConfigureProvider(DbContextOptionsBuilder<FlowTestDbContext> optionsBuilder);

    private FlowTestDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<FlowTestDbContext>();
        ConfigureProvider(optionsBuilder);
        return new FlowTestDbContext(optionsBuilder.Options, _schema);
    }

    protected string ConnectionString => _container.GetConnectionString();
}

public sealed class PostgreSqlFlowTestDriver : EntityFrameworkCoreFlowTestDriver
{
    public PostgreSqlFlowTestDriver()
        : base(
            new PostgreSqlBuilder().WithDatabase("atomizer").WithUsername("postgres").WithPassword("secret").Build(),
            "Atomizer"
        ) { }

    public override string Name => "PostgreSQL";

    protected override void ConfigureProvider(DbContextOptionsBuilder<FlowTestDbContext> optionsBuilder)
    {
        optionsBuilder.UseNpgsql(ConnectionString);
    }
}

public sealed class MySqlFlowTestDriver : EntityFrameworkCoreFlowTestDriver
{
    public MySqlFlowTestDriver()
        : base(
            new MySqlBuilder().WithDatabase("atomizer").WithUsername("root").WithPassword("secret").Build(),
            schema: null
        ) { }

    public override string Name => "MySQL";

    protected override void ConfigureProvider(DbContextOptionsBuilder<FlowTestDbContext> optionsBuilder)
    {
        optionsBuilder.UseMySql(ConnectionString, ServerVersion.AutoDetect(ConnectionString));
    }
}

public sealed class SqlServerFlowTestDriver : EntityFrameworkCoreFlowTestDriver
{
    public SqlServerFlowTestDriver()
        : base(new MsSqlBuilder().Build(), "Atomizer") { }

    public override string Name => "SQL Server";

    protected override void ConfigureProvider(DbContextOptionsBuilder<FlowTestDbContext> optionsBuilder)
    {
        optionsBuilder.UseSqlServer(ConnectionString);
    }
}

[CollectionDefinition(nameof(PostgreSqlFlowTestDriver))]
public sealed class PostgreSqlFlowTestCollection : ICollectionFixture<PostgreSqlFlowTestDriver>;

[CollectionDefinition(nameof(MySqlFlowTestDriver))]
public sealed class MySqlFlowTestCollection : ICollectionFixture<MySqlFlowTestDriver>;

[CollectionDefinition(nameof(SqlServerFlowTestDriver))]
public sealed class SqlServerFlowTestCollection : ICollectionFixture<SqlServerFlowTestDriver>;
