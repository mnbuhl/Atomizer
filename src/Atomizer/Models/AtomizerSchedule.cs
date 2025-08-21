using System;

namespace Atomizer.Models
{
    public class AtomizerSchedule
    {
        public Guid Id { get; set; }
        public JobKey JobKey { get; set; } = new JobKey("default");
        public QueueKey QueueKey { get; set; } = QueueKey.Default;
        public Type PayloadType { get; set; } = null!;
        public string Payload { get; set; } = string.Empty;
        public string CronExpression { get; set; } = "0 0 0 * *"; // Daily at midnight
        public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;
        public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.ExecuteNow;
        public int MaxCatchUp { get; set; } = 5; // Default to catching up 5 missed runs
        public bool Enabled { get; set; } = true;
        public int MaxAttempts { get; set; } = 3;
        public DateTimeOffset NextRunAt { get; set; }
        public DateTimeOffset? LastEnqueueAt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public LeaseToken? LeaseToken { get; set; }
        public DateTimeOffset? VisibleAt { get; set; }
    }

    public enum MisfirePolicy
    {
        Ignore = 1, // skip this run; advance to next
        ExecuteNow = 2, // enqueue one now; then advance one step
        CatchUp = 3, // enqueue all missed (bounded by MaxCatchUp)
    }
}
