using System;
using System.Collections.Generic;
using System.Linq;
using Atomizer.Models.Base;
using Cronos;

namespace Atomizer
{
    public class AtomizerSchedule : Model
    {
        public JobKey JobKey { get; set; } = new JobKey("default");
        public QueueKey QueueKey { get; set; } = QueueKey.Default;
        public Type? PayloadType { get; set; }
        public string Payload { get; set; } = string.Empty;
        public Schedule Schedule { get; set; } = Schedule.Default;
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

        private CronExpression CronExpression => CronExpression.Parse(Schedule.ToString(), CronFormat.IncludeSeconds);

        public static AtomizerSchedule Create(
            JobKey jobKey,
            QueueKey queueKey,
            Type payloadType,
            string payload,
            Schedule schedule,
            TimeZoneInfo timeZone,
            DateTimeOffset createdAt,
            MisfirePolicy misfirePolicy = MisfirePolicy.ExecuteNow,
            int maxCatchUp = 5,
            bool enabled = true,
            int maxAttempts = 3
        )
        {
            var atomizerSchedule = new AtomizerSchedule
            {
                Id = Guid.NewGuid(),
                JobKey = jobKey,
                QueueKey = queueKey,
                PayloadType = payloadType,
                Payload = payload,
                Schedule = schedule,
                TimeZone = timeZone,
                MisfirePolicy = misfirePolicy,
                MaxCatchUp = maxCatchUp,
                Enabled = enabled,
                MaxAttempts = maxAttempts,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
            };

            atomizerSchedule.NextRunAt =
                atomizerSchedule.CronExpression.GetNextOccurrence(createdAt, timeZone) ?? DateTimeOffset.MaxValue;

            return atomizerSchedule;
        }

        public List<DateTimeOffset> GetOccurrences(DateTimeOffset now)
        {
            var occurrences = new List<DateTimeOffset>();

            if (NextRunAt > now)
            {
                return occurrences;
            }

            switch (MisfirePolicy)
            {
                case MisfirePolicy.Ignore:
                    break;
                case MisfirePolicy.ExecuteNow:
                    occurrences.Add(NextRunAt);
                    break;
                case MisfirePolicy.CatchUp:
                    occurrences.AddRange(
                        CronExpression
                            .GetOccurrences(LastEnqueueAt ?? CreatedAt, now, TimeZone)
                            .OrderBy(dt => dt)
                            .Take(MaxCatchUp)
                    );
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(MisfirePolicy),
                        MisfirePolicy,
                        "Invalid misfire policy"
                    );
            }

            return occurrences;
        }

        public void UpdateNextOccurenceAndRelease(DateTimeOffset horizon, DateTimeOffset now)
        {
            var nextOccurrence = CronExpression.GetNextOccurrence(horizon, TimeZone);
            NextRunAt = nextOccurrence ?? DateTimeOffset.MaxValue; // No further occurrences
            LastEnqueueAt = horizon;
            LeaseToken = null;
            VisibleAt = null;
            UpdatedAt = now;
        }

        public void Disable(DateTimeOffset now)
        {
            Enabled = false;
            UpdatedAt = now;
        }
    }

    public enum MisfirePolicy
    {
        Ignore = 1, // skip this run; advance to next
        ExecuteNow = 2, // enqueue one now; then advance one step
        CatchUp = 3, // enqueue all missed (bounded by MaxCatchUp)
    }
}
