using System;

namespace Atomizer.Models
{
    public class AtomizerRecurringJob
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string QueueKey { get; set; } = string.Empty;
        public string CronExpression { get; set; } = string.Empty;
        public string TimeZoneId { get; set; } = string.Empty;
        public string PayloadType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public int MaxAttempts { get; set; } = 3;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset NextOccurrence { get; set; }
        public DateTimeOffset? LastOccurrence { get; set; }
        public bool Paused { get; set; }
        public AtomizerEntityMisfirePolicy EntityMisfirePolicy { get; set; } = AtomizerEntityMisfirePolicy.RunNow;
        public AtomizerEntityConcurrencyPolicy ConcurrencyPolicy { get; set; } = AtomizerEntityConcurrencyPolicy.Allow;
        public string? LeaseToken { get; set; }
    }

    public enum AtomizerEntityMisfirePolicy
    {
        Ignore = 1,
        RunNow = 2,
        CatchUp = 3,
    }

    public enum AtomizerEntityConcurrencyPolicy
    {
        Allow = 1,
        SkipIfRunning = 2,
    }
}
