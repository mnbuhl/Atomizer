using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atomizer.EntityFrameworkCore.Configurations;

/// <summary>
/// Entity Framework Core configuration for <see cref="AtomizerScheduleEntity"/>.
/// </summary>
public class AtomizerScheduleEntityConfiguration : IEntityTypeConfiguration<AtomizerScheduleEntity>
{
    private readonly string? _schema;

    public AtomizerScheduleEntityConfiguration(string? schema)
    {
        _schema = schema;
    }

    public void Configure(EntityTypeBuilder<AtomizerScheduleEntity> builder)
    {
        builder.ToTable("AtomizerSchedules", _schema);
        builder.HasKey(e => e.Id);
        builder.Property(job => job.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.JobKey).IsRequired().HasMaxLength(512);
        builder.Property(e => e.QueueKey).IsRequired().HasMaxLength(512);
        builder.Property(e => e.PayloadType).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.Payload).IsRequired();
        builder.Property(e => e.Schedule).IsRequired().HasMaxLength(1024);
        builder.Property(e => e.TimeZone).IsRequired().HasMaxLength(64);
        builder.Property(e => e.MisfirePolicy).IsRequired();
        builder.Property(e => e.MaxCatchUp).IsRequired();
        builder.Property(e => e.Enabled).IsRequired();
        builder.Property(e => e.NextRunAt).IsRequired();
        builder.Property(e => e.LastEnqueueAt);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt).IsRequired();
        builder
            .Property(job => job.RetryIntervals)
            .IsRequired()
            .HasMaxLength(4096)
            .HasConversion(
                v => string.Join(';', v.Select(ts => (long)ts.TotalMilliseconds)),
                v =>
                    v.Split(';', StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => TimeSpan.FromMilliseconds(long.Parse(s)))
                        .ToArray(),
                new ValueComparer<TimeSpan[]>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToArray()
                )
            );
    }
}
