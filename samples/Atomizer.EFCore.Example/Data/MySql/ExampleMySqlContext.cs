using Atomizer.EFCore.Example.Entities;
using Atomizer.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EFCore.Example.Data.MySql;

public class ExampleMySqlContext(DbContextOptions<ExampleMySqlContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddAtomizerEntities(schema: null);

        base.OnModelCreating(modelBuilder);
    }
}
