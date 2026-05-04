using System.Collections.Concurrent;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers.Sql;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Providers;

internal sealed class RelationalProviderCache
{
    public bool IsSupportedProvider => _databaseProvider is not null;
    public string ProviderName { get; }
    public ISqlDialect? Dialect { get; }

    private readonly DatabaseProvider? _databaseProvider;
    private readonly EntityMap? _jobs;
    private readonly EntityMap? _schedules;

    private RelationalProviderCache(
        string providerName,
        DatabaseProvider? databaseProvider,
        EntityMap? jobs,
        EntityMap? schedules
    )
    {
        ProviderName = providerName;
        _databaseProvider = databaseProvider;
        _jobs = jobs;
        _schedules = schedules;

        if (IsSupportedProvider)
        {
            Dialect = CreateDialect();
        }
    }

    private static readonly ConcurrentDictionary<string, RelationalProviderCache> Instances = new();

    public static RelationalProviderCache Create<TDbContext>(TDbContext dbContext)
        where TDbContext : DbContext
    {
        var providerName = dbContext.Database.ProviderName ?? string.Empty;
        var provider = DetectProvider(providerName);

        return Instances.GetOrAdd(
            providerName,
            _ =>
            {
                EntityMap? jobs = null,
                    schedules = null;

                if (provider is not null)
                {
                    var model = dbContext.Model; // capture once
                    jobs = EntityMap.Build(model, typeof(AtomizerJobEntity), provider.Value);
                    schedules = EntityMap.Build(model, typeof(AtomizerScheduleEntity), provider.Value);
                }

                return new RelationalProviderCache(providerName, provider, jobs, schedules);
            }
        );
    }

    private ISqlDialect CreateDialect()
    {
        if (!IsSupportedProvider || _jobs is null || _schedules is null)
        {
            throw new InvalidOperationException("Database provider is not supported or entity mappings are missing.");
        }

        return _databaseProvider switch
        {
            DatabaseProvider.PostgreSql => new PostgreSqlDialect(_jobs, _schedules),
            DatabaseProvider.MySql => new MySqlDialect(_jobs, _schedules),
            DatabaseProvider.SqlServer => new SqlServerDialect(_jobs, _schedules),
            _ => throw new NotSupportedException($"Database provider {_databaseProvider} is not supported."),
        };
    }

    private static DatabaseProvider? DetectProvider(string name) =>
        name switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => DatabaseProvider.SqlServer,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => DatabaseProvider.PostgreSql,
            "Pomelo.EntityFrameworkCore.MySql" or "MySql.EntityFrameworkCore" => DatabaseProvider.MySql,
            _ => null,
        };

    // Testing helpers
    internal static bool TryGet(DatabaseProvider provider, out RelationalProviderCache? cache) =>
        Instances.TryGetValue(GetProviderName(provider), out cache);

    internal static void ResetInstanceForTests(DatabaseProvider? provider = null)
    {
        if (provider is null)
        {
            Instances.Clear();
            return;
        }

        Instances.TryRemove(GetProviderName(provider.Value), out _);
    }

    private static string GetProviderName(DatabaseProvider provider) =>
        provider switch
        {
            DatabaseProvider.SqlServer => "Microsoft.EntityFrameworkCore.SqlServer",
            DatabaseProvider.PostgreSql => "Npgsql.EntityFrameworkCore.PostgreSQL",
            DatabaseProvider.MySql => "Pomelo.EntityFrameworkCore.MySql",
            _ => throw new NotSupportedException($"Database provider {provider} is not supported."),
        };
}
