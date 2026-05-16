using StackExchange.Redis;
using Testcontainers.Redis;

namespace Atomizer.Redis.Tests.Storage;

public sealed class RedisStorageFixture : IAsyncLifetime
{
    private readonly RedisContainer _container = new RedisBuilder("redis:7-alpine").Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var configuration = ConfigurationOptions.Parse(_container.GetConnectionString());
        configuration.AllowAdmin = true;
        Connection = await ConnectionMultiplexer.ConnectAsync(configuration);
    }

    public async ValueTask DisposeAsync()
    {
        if (Connection is not null)
            await Connection.DisposeAsync();

        await _container.DisposeAsync();
    }

    public async Task FlushAsync()
    {
        var endpoints = Connection.GetEndPoints();
        foreach (var endpoint in endpoints)
        {
            var server = Connection.GetServer(endpoint);
            await server.FlushDatabaseAsync();
        }
    }
}

[CollectionDefinition(nameof(RedisStorageFixture))]
public sealed class RedisStorageCollection : ICollectionFixture<RedisStorageFixture>;
