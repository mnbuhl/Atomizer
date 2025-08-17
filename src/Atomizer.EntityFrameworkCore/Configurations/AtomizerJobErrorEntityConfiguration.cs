using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atomizer.EntityFrameworkCore.Configurations;

public class AtomizerJobErrorEntityConfiguration : IEntityTypeConfiguration<AtomizerJobErrorEntity>
{
    private readonly string _schema;

    public AtomizerJobErrorEntityConfiguration(string schema)
    {
        _schema = schema;
    }

    public void Configure(EntityTypeBuilder<AtomizerJobErrorEntity> builder)
    {
        builder.ToTable("AtomizerJobErrors", _schema);
        builder.HasKey(error => error.Id);
        builder.Property(error => error.Id).ValueGeneratedNever();
        builder.Property(error => error.JobId).IsRequired();
        builder.Property(error => error.ErrorMessage).HasMaxLength(1024);
        builder.Property(error => error.StackTrace).HasMaxLength(5120);
        builder.Property(error => error.CreatedAt).IsRequired();
        builder.Property(error => error.Attempt).IsRequired();
        builder.Property(error => error.RuntimeIdentity).HasMaxLength(255);

        // Configure the relationship with AtomizerJobEntity
        builder
            .HasOne(error => error.Job)
            .WithMany(job => job.Errors)
            .HasForeignKey(error => error.JobId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
