using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.SqlServer;

public class SqlServerDbContext : TestDbContext
{
    public SqlServerDbContext(DbContextOptions<SqlServerDbContext> options, string? schema = null)
        : base(options, schema) { }
}
