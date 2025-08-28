using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.MySql;

public class MySqlDbContext : TestDbContext
{
    public MySqlDbContext(DbContextOptions<MySqlDbContext> options, string? schema = null)
        : base(options, schema) { }
}
