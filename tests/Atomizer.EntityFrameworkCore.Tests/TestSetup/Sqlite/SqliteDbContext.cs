using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.Sqlite;

public class SqliteDbContext : TestDbContext
{
    public SqliteDbContext(DbContextOptions<SqliteDbContext> options, string? schema = null)
        : base(options, schema) { }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        base.ConfigureConventions(configurationBuilder);
    }
}
