using Atomizer.EFCore.Example.Entities;
using Atomizer.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EFCore.Example.Data.Sqlite;

public class ExampleSqliteContext(DbContextOptions<ExampleSqliteContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddAtomizerEntities();

        base.OnModelCreating(modelBuilder);
    }
}
