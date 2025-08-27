using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Tests.TestSetup.MySql;

public class MySqlDbContext : TestDbContext
{
    public MySqlDbContext(DbContextOptions<TestDbContext> options, string? schema = null)
        : base(options, schema) { }
}
