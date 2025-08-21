using Atomizer.EFCore.Example.Entities;
using Atomizer.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EFCore.Example.Data.SqlServer;

public class ExampleSqlServerContext(DbContextOptions<ExampleSqlServerContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddAtomizerEntities();

        base.OnModelCreating(modelBuilder);
    }
}
