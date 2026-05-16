using Atomizer.Redis;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Atomizer.FlowTests.Drivers;

public sealed class RedisFlowTestDriver : FlowTestDriver
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    public override string Name => "Redis";

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public override async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var configuration = ConfigurationOptions.Parse(_container.GetConnectionString());
        configuration.AllowAdmin = true;
        Connection = await ConnectionMultiplexer.ConnectAsync(configuration);
    }

    public override async ValueTask DisposeAsync()
    {
        if (Connection is not null)
            await Connection.DisposeAsync();

        await _container.DisposeAsync();
    }

    public override void ConfigureStorage(AtomizerOptions options, string runId)
    {
        options.UseRedisStorage(Connection, storage => storage.KeyPrefix = $"flow:{runId}");
    }

    public override async Task ResetAsync(CancellationToken cancellationToken)
    {
        foreach (var endpoint in Connection.GetEndPoints())
        {
            var server = Connection.GetServer(endpoint);
            await server.FlushDatabaseAsync();
        }
    }
}

[CollectionDefinition(nameof(RedisFlowTestDriver))]
public sealed class RedisFlowTestCollection : ICollectionFixture<RedisFlowTestDriver>;
