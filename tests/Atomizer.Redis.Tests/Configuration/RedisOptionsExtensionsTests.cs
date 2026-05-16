using Atomizer.Abstractions;
using Atomizer.Redis;
using Atomizer.Redis.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;

namespace Atomizer.Redis.Tests.Configuration;

public sealed class RedisOptionsExtensionsTests
{
    [Fact]
    public void UseRedisStorage_WhenConnectionRegisteredInServices_ShouldRegisterRedisStorage()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IConnectionMultiplexer>());

        services.AddAtomizer(options =>
        {
            options.UseRedisStorage(redis => redis.KeyPrefix = $"config:{Guid.NewGuid():N}");
        });

        using var provider = services.BuildServiceProvider();

        provider.GetService<IAtomizerStorage>().Should().BeOfType<RedisStorage>();
        provider.GetService<IConnectionMultiplexer>().Should().NotBeNull();
    }
}
