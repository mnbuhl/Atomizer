using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.Sqlite;

public class SqliteDbContext : TestDbContext
{
    public SqliteDbContext(DbContextOptions<SqliteDbContext> options, string? schema = null)
        : base(options, schema) { }
}
