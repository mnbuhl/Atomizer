using System.Collections.Concurrent;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers.Sql;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Providers;

internal sealed class RelationalProviderCache
{
    public bool IsSupportedProvider => DetermineSupportedProvider(DatabaseProvider);
    public ISqlDialect? Dialect { get; }

    private DatabaseProvider DatabaseProvider { get; }
    private readonly EntityMap? _jobs;
    private readonly EntityMap? _schedules;
    private readonly EntityMap? _activeServers;

    private RelationalProviderCache(DatabaseProvider databaseProvider, EntityMap? jobs, EntityMap? schedules, EntityMap? activeServers)
    {
        DatabaseProvider = databaseProvider;
        _jobs = jobs;
        _schedules = schedules;
        _activeServers = activeServers;

        if (IsSupportedProvider)
        {
            Dialect = CreateDialect();
        }
    }

    private static readonly ConcurrentDictionary<DatabaseProvider, RelationalProviderCache> Instances = new();

    public static RelationalProviderCache Create<TDbContext>(TDbContext dbContext)
        where TDbContext : DbContext
    {
        // Determine provider for this DbContext
        var provider = DetectProvider(dbContext.Database.ProviderName ?? string.Empty);

        // Freeze Unknown per-provider as well (same semantics as before, but keyed).
        return Instances.GetOrAdd(
            provider,
            _ =>
            {
                EntityMap? jobs = null,
                    schedules = null,
                    activeServers = null;

                if (DetermineSupportedProvider(provider))
                {
                    var model = dbContext.Model; // capture once
                    jobs = EntityMap.Build(model, typeof(AtomizerJobEntity), provider);
                    schedules = EntityMap.Build(model, typeof(AtomizerScheduleEntity), provider);
                    activeServers = EntityMap.Build(model, typeof(AtomizerActiveServerEntity), provider);
                }

                return new RelationalProviderCache(provider, jobs, schedules, activeServers);
            }
        );
    }

    private ISqlDialect CreateDialect()
    {
        if (!IsSupportedProvider || _jobs is null || _schedules is null || _activeServers is null)
        {
            throw new InvalidOperationException("Database provider is not supported or entity mappings are missing.");
        }

        return DatabaseProvider switch
        {
            DatabaseProvider.PostgreSql => new PostgreSqlDialect(_jobs, _schedules, _activeServers),
            DatabaseProvider.MySql => new MySqlDialect(_jobs, _schedules, _activeServers),
            DatabaseProvider.SqlServer => new SqlServerDialect(_jobs, _schedules, _activeServers),
            _ => throw new NotSupportedException($"Database provider {DatabaseProvider} is not supported."),
        };
    }

    private static DatabaseProvider DetectProvider(string name) =>
        name switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => DatabaseProvider.SqlServer,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => DatabaseProvider.PostgreSql,
            "Pomelo.EntityFrameworkCore.MySql" or "MySql.EntityFrameworkCore" => DatabaseProvider.MySql,
            "Oracle.EntityFrameworkCore" => DatabaseProvider.Oracle,
            "Microsoft.EntityFrameworkCore.Sqlite" => DatabaseProvider.Sqlite,
            _ => DatabaseProvider.Unknown,
        };

    private static bool DetermineSupportedProvider(DatabaseProvider provider)
    {
        return provider is DatabaseProvider.PostgreSql or DatabaseProvider.MySql or DatabaseProvider.SqlServer;
    }

    // Testing helpers
    internal static bool TryGet(DatabaseProvider provider, out RelationalProviderCache? cache) =>
        Instances.TryGetValue(provider, out cache);

    internal static void ResetInstanceForTests(DatabaseProvider? provider = null)
    {
        if (provider is null)
        {
            Instances.Clear();
            return;
        }

        Instances.TryRemove(provider.Value, out _);
    }
}
