using Atomizer.Core;
using Atomizer.Redis.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Atomizer.Redis;

/// <summary>
/// Extension methods for configuring the Redis storage backend with Atomizer.
/// </summary>
public static class AtomizerOptionsExtensions
{
    /// <summary>
    /// Configures Atomizer to use Redis storage with an <see cref="IConnectionMultiplexer"/> resolved from DI.
    /// </summary>
    /// <param name="options">The <see cref="AtomizerOptions"/> to configure.</param>
    /// <param name="configure">Optional delegate to configure <see cref="RedisJobStorageOptions"/>.</param>
    /// <returns>The <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public static AtomizerOptions UseRedisStorage(
        this AtomizerOptions options,
        Action<RedisJobStorageOptions>? configure = null
    )
    {
        var redisOptions = CreateOptions(configure);
        options.JobStorageOptions = new JobStorageOptions(sp => new RedisStorage(
            sp.GetRequiredService<IConnectionMultiplexer>(),
            redisOptions,
            sp.GetRequiredService<IAtomizerClock>(),
            sp.GetRequiredService<ILogger<RedisStorage>>()
        ));

        return options;
    }

    /// <summary>
    /// Configures Atomizer to use Redis storage with a connection string.
    /// </summary>
    /// <param name="options">The <see cref="AtomizerOptions"/> to configure.</param>
    /// <param name="configuration">The StackExchange.Redis connection string.</param>
    /// <param name="configure">Optional delegate to configure <see cref="RedisJobStorageOptions"/>.</param>
    /// <returns>The <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public static AtomizerOptions UseRedisStorage(
        this AtomizerOptions options,
        string configuration,
        Action<RedisJobStorageOptions>? configure = null
    )
    {
        if (string.IsNullOrWhiteSpace(configuration))
            throw new ArgumentException("Redis connection string cannot be null or empty.", nameof(configuration));

        var redisOptions = CreateOptions(configure);
        options.JobStorageOptions = new JobStorageOptions(sp => new RedisStorage(
            ConnectionMultiplexer.Connect(configuration),
            redisOptions,
            sp.GetRequiredService<IAtomizerClock>(),
            sp.GetRequiredService<ILogger<RedisStorage>>(),
            ownsConnection: true
        ));

        return options;
    }

    /// <summary>
    /// Configures Atomizer to use Redis storage with an existing StackExchange.Redis connection.
    /// </summary>
    /// <param name="options">The <see cref="AtomizerOptions"/> to configure.</param>
    /// <param name="connection">The Redis connection multiplexer to use.</param>
    /// <param name="configure">Optional delegate to configure <see cref="RedisJobStorageOptions"/>.</param>
    /// <returns>The <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public static AtomizerOptions UseRedisStorage(
        this AtomizerOptions options,
        IConnectionMultiplexer connection,
        Action<RedisJobStorageOptions>? configure = null
    )
    {
        if (connection is null)
            throw new ArgumentNullException(nameof(connection));

        var redisOptions = CreateOptions(configure);
        options.JobStorageOptions = new JobStorageOptions(sp => new RedisStorage(
            connection,
            redisOptions,
            sp.GetRequiredService<IAtomizerClock>(),
            sp.GetRequiredService<ILogger<RedisStorage>>()
        ));

        return options;
    }

    /// <summary>
    /// Configures Atomizer to use Redis storage with a connection factory.
    /// </summary>
    /// <param name="options">The <see cref="AtomizerOptions"/> to configure.</param>
    /// <param name="connectionFactory">Factory used to resolve the Redis connection multiplexer.</param>
    /// <param name="configure">Optional delegate to configure <see cref="RedisJobStorageOptions"/>.</param>
    /// <returns>The <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public static AtomizerOptions UseRedisStorage(
        this AtomizerOptions options,
        Func<IServiceProvider, IConnectionMultiplexer> connectionFactory,
        Action<RedisJobStorageOptions>? configure = null
    )
    {
        if (connectionFactory is null)
            throw new ArgumentNullException(nameof(connectionFactory));

        var redisOptions = CreateOptions(configure);
        options.JobStorageOptions = new JobStorageOptions(sp => new RedisStorage(
            connectionFactory(sp),
            redisOptions,
            sp.GetRequiredService<IAtomizerClock>(),
            sp.GetRequiredService<ILogger<RedisStorage>>()
        ));

        return options;
    }

    private static RedisJobStorageOptions CreateOptions(Action<RedisJobStorageOptions>? configure)
    {
        var options = new RedisJobStorageOptions();
        configure?.Invoke(options);
        options.Validate();
        return options;
    }
}
