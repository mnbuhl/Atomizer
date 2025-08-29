using System.Collections.Concurrent;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers.Sql;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Providers;

internal sealed class RelationalProviderCache
{
    public bool IsSupportedProvider => DetermineSupportedProvider(DatabaseProvider);
    public IDatabaseProviderSql? RawSqlProvider { get; }

    private DatabaseProvider DatabaseProvider { get; }
    private readonly EntityMap? _jobs;
    private readonly EntityMap? _schedules;

    private RelationalProviderCache(DatabaseProvider databaseProvider, EntityMap? jobs, EntityMap? schedules)
    {
        DatabaseProvider = databaseProvider;
        _jobs = jobs;
        _schedules = schedules;

        if (IsSupportedProvider)
        {
            RawSqlProvider = CreateRawSqlProvider();
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
                    schedules = null;

                if (DetermineSupportedProvider(provider))
                {
                    var model = dbContext.Model; // capture once
                    jobs = EntityMap.Build(model, typeof(AtomizerJobEntity), provider);
                    schedules = EntityMap.Build(model, typeof(AtomizerScheduleEntity), provider);
                }

                return new RelationalProviderCache(provider, jobs, schedules);
            }
        );
    }

    private IDatabaseProviderSql CreateRawSqlProvider()
    {
        if (!IsSupportedProvider || _jobs is null || _schedules is null)
        {
            throw new InvalidOperationException("Database provider is not supported or entity mappings are missing.");
        }

        return DatabaseProvider switch
        {
            DatabaseProvider.PostgreSql => new PostgreSqlProvider(_jobs, _schedules),
            DatabaseProvider.MySql => new MySqlProvider(_jobs, _schedules),
            DatabaseProvider.SqlServer => new SqlServerProvider(_jobs, _schedules),
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
