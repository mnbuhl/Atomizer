using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers.Sql;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Providers;

internal sealed class RelationalProviderCache
{
    public bool IsSupportedProvider =>
        _databaseProvider != DatabaseProvider.Unknown && _databaseProvider != DatabaseProvider.Sqlite;
    public IDatabaseProviderSql? RawSqlProvider { get; }

    private readonly DatabaseProvider _databaseProvider;
    private readonly EntityMap? _jobs;
    private readonly EntityMap? _schedules;

    private RelationalProviderCache(DatabaseProvider databaseProvider, EntityMap? jobs, EntityMap? schedules)
    {
        _databaseProvider = databaseProvider;
        _jobs = jobs;
        _schedules = schedules;

        if (IsSupportedProvider)
        {
            RawSqlProvider = CreateRawSqlProvider();
        }
    }

    private static RelationalProviderCache? _instance;

    public static RelationalProviderCache Create<TDbContext>(TDbContext dbContext)
        where TDbContext : DbContext
    {
        // Fast-path: already initialized
        var existing = Volatile.Read(ref _instance);

        if (existing is not null)
        {
            return existing;
        }

        // First caller decides. If provider is Unknown, we freeze as Unknown forever (by design).
        var provider = DetectProvider(dbContext.Database.ProviderName ?? string.Empty);

        EntityMap? jobs = null,
            schedules = null;
        if (provider != DatabaseProvider.Unknown)
        {
            var model = dbContext.Model; // capture once
            jobs = EntityMap.Build(model, typeof(AtomizerJobEntity), provider);
            schedules = EntityMap.Build(model, typeof(AtomizerScheduleEntity), provider);
        }

        var created = new RelationalProviderCache(provider, jobs, schedules);

        // Publish if not already set; otherwise return the winner.
        var winner = Interlocked.CompareExchange(ref _instance, created, null);
        return winner ?? created;
    }

    private IDatabaseProviderSql CreateRawSqlProvider()
    {
        if (!IsSupportedProvider || _jobs is null || _schedules is null)
        {
            throw new InvalidOperationException("Database provider is not supported or entity mappings are missing.");
        }

        return _databaseProvider switch
        {
            DatabaseProvider.PostgreSql => new PostgreSqlProvider(_jobs, _schedules),
            DatabaseProvider.MySql => new MySqlProvider(_jobs, _schedules),
            DatabaseProvider.SqlServer => new SqlServerProvider(_jobs, _schedules),
            DatabaseProvider.Oracle => new OracleProvider(_jobs, _schedules),
            _ => throw new NotSupportedException($"Database provider {_databaseProvider} is not supported."),
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

    internal static void ResetInstanceForTests() => Interlocked.Exchange(ref _instance, null);
}
