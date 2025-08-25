namespace Atomizer;

public class SchedulingOptions
{
    /// <summary>
    /// Gets or sets the visibility timeout for jobs in the queue.
    /// <remarks>Default is 10 minutes, meaning that once a job is picked up for processing, it will not be visible to other workers for 10 minutes.</remarks>
    /// </summary>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the interval at which the atomizer checks for storage updates.
    /// <remarks>Default is 15 seconds, meaning that the atomizer will check for storage updates every 15 seconds.</remarks>
    /// </summary>
    public TimeSpan StorageCheckInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets the lead time for scheduling jobs. It will enqueue jobs ahead of the next scheduled time.
    /// I.e. if ScheduleLeadTime is 15 seconds, jobs will be enqueued 15 seconds before the next scheduled time.
    /// <remarks>Default is max(StorageCheckInterval, 1s)</remarks>
    /// </summary>
    public TimeSpan? ScheduleLeadTime { get; set; }

    /// <summary>
    /// The interval at which the long running loops tick.
    /// </summary>
    public TimeSpan TickInterval { get; internal set; } = TimeSpan.FromSeconds(1);

    public SchedulingOptions()
    {
        ScheduleLeadTime ??=
            StorageCheckInterval > TimeSpan.FromSeconds(1) ? StorageCheckInterval : TimeSpan.FromSeconds(1);
    }
}
