using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atomizer.EntityFrameworkCore.Configurations
{
    public class AtomizerJobEntityConfiguration : IEntityTypeConfiguration<AtomizerJobEntity>
    {
        private readonly string _schema;

        public AtomizerJobEntityConfiguration(string schema)
        {
            _schema = schema;
        }

        public void Configure(EntityTypeBuilder<AtomizerJobEntity> builder)
        {
            builder.ToTable("AtomizerJobs", _schema);
            builder.HasKey(job => job.Id);
            builder.Property(job => job.Id).ValueGeneratedOnAdd();
            builder.Property(job => job.QueueKey).IsRequired().HasMaxLength(512);
            builder.Property(job => job.PayloadType).IsRequired().HasMaxLength(1024);
            builder.Property(job => job.Payload).IsRequired();
            builder.Property(job => job.ScheduledAt).IsRequired();
            builder.Property(job => job.VisibleAt).IsRequired(false);
            builder.Property(job => job.Status).IsRequired();
            builder.Property(job => job.Attempts).IsRequired();
            builder.Property(job => job.MaxAttempts).IsRequired();
            builder.Property(job => job.CreatedAt).IsRequired();
            builder.Property(job => job.CompletedAt).IsRequired(false);
            builder.Property(job => job.FailedAt).IsRequired(false);
            builder.Property(job => job.IdempotencyKey).HasMaxLength(255);
            builder.Property(job => job.LeaseToken).HasMaxLength(512);
        }
    }
}
