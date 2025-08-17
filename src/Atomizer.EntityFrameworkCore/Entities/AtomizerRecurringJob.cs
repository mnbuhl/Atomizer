using System;
using Atomizer.Models;

namespace Atomizer.EntityFrameworkCore.Entities
{
    public class AtomizerRecurringJobEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public QueueKey QueueKey { get; set; } = QueueKey.Default;
        public string CronExpression { get; set; } = string.Empty;
        public TimeZoneInfo TimeZoneId { get; set; } = TimeZoneInfo.Utc;
        public Type PayloadType { get; set; } = null!;
        public string Payload { get; set; } = string.Empty;
        public int MaxAttempts { get; set; } = 3;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset NextOccurrence { get; set; }
        public DateTimeOffset? LastOccurrence { get; set; }
        public bool Paused { get; set; }
        public AtomizerMisfirePolicy MisfirePolicy { get; set; } = AtomizerMisfirePolicy.RunNow;
        public AtomizerConcurrencyPolicy ConcurrencyPolicy { get; set; } = AtomizerConcurrencyPolicy.Allow;
        public LeaseToken? LeaseToken { get; set; }
    }

    public enum AtomizerMisfirePolicy
    {
        Ignore = 1,
        RunNow = 2,
        CatchUp = 3,
    }

    public enum AtomizerConcurrencyPolicy
    {
        Allow = 1,
        SkipIfRunning = 2,
    }
}
