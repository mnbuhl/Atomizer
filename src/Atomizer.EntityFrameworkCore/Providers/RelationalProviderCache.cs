using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Providers;

internal sealed class RelationalProviderCache
{
    private RelationalDatabaseProvider _databaseProvider;
    private readonly EntityMap? _jobs;
    private readonly EntityMap? _schedules;

    private RelationalProviderCache(RelationalDatabaseProvider databaseProvider, EntityMap jobs, EntityMap schedules)
    {
        _databaseProvider = databaseProvider;
        _jobs = jobs;
        _schedules = schedules;
    }

    private RelationalProviderCache()
    {
        _databaseProvider = RelationalDatabaseProvider.Unknown;
    }

    public static RelationalProviderCache Create<TDbContext>(TDbContext dbContext)
        where TDbContext : DbContext
    {
        var provider = DetectProvider(dbContext.Database.ProviderName!);

        if (provider == RelationalDatabaseProvider.Unknown)
        {
            return new RelationalProviderCache();
        }

        var jobs = EntityMap.Build(dbContext.Model, typeof(AtomizerJobEntity), provider);
        var schedules = EntityMap.Build(dbContext.Model, typeof(AtomizerScheduleEntity), provider);

        return new RelationalProviderCache(provider, jobs, schedules);
    }

    private static RelationalDatabaseProvider DetectProvider(string name) =>
        name switch
        {
            "Microsoft.EntityFrameworkCore.SqlServer" => RelationalDatabaseProvider.SqlServer,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => RelationalDatabaseProvider.PostgreSql,
            "Pomelo.EntityFrameworkCore.MySql" or "MySql.EntityFrameworkCore" => RelationalDatabaseProvider.MySql,
            "Oracle.EntityFrameworkCore" => RelationalDatabaseProvider.Oracle,
            _ => RelationalDatabaseProvider.Unknown,
        };
}
