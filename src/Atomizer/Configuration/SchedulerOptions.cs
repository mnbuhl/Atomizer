using System;
using Atomizer.Models;

namespace Atomizer.Configuration
{
    public class SchedulerOptions
    {
        /// <summary>
        /// Gets or sets the interval at which the atomizer checks for storage updates.
        /// <remarks>Default is 15 seconds, meaning that the atomizer will check for storage updates every 15 seconds.</remarks>
        /// </summary>
        public TimeSpan StorageCheckInterval { get; set; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Gets or sets the default time zone for scheduling jobs.
        /// <remarks>Default is UTC, meaning that all scheduled jobs will be interpreted in UTC time.</remarks>
        /// </summary>
        public TimeZoneInfo DefaultTimeZone { get; set; } = TimeZoneInfo.Utc;

        /// <summary>
        /// Gets or sets the default queue for scheduling jobs.
        /// <remarks>Default is QueueKey.Default, meaning that all jobs will be scheduled in the default queue.</remarks>
        /// </summary>
        public QueueKey DefaultQueue { get; set; } = QueueKey.Default;
    }
}
