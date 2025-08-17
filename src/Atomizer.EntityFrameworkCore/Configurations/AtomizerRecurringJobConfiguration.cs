using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atomizer.EntityFrameworkCore.Configurations;

public class AtomizerRecurringJobConfiguration : IEntityTypeConfiguration<AtomizerRecurringJobEntity>
{
    public void Configure(EntityTypeBuilder<AtomizerRecurringJobEntity> builder)
    {
        throw new System.NotImplementedException();
    }
}
