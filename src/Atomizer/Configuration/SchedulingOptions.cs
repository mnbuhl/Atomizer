namespace Atomizer;

/// <summary>
/// Configures the Atomizer scheduling subsystem.
/// </summary>
public class SchedulingOptions
{
    /// <summary>
    /// Gets or sets the visibility timeout for jobs in the queue.
    /// </summary>
    /// <remarks>Default is 10 minutes, meaning that once a job is picked up for processing, it will not be visible to other workers for 10 minutes.</remarks>
    public TimeSpan VisibilityTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Gets or sets the interval at which the atomizer checks for storage updates.
    /// </summary>
    /// <remarks>Default is 15 seconds, meaning that the atomizer will check for storage updates every 15 seconds.</remarks>
    public TimeSpan StorageCheckInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets the lead time for scheduling jobs. It will enqueue jobs ahead of the next scheduled time.
    /// I.e. if ScheduleLeadTime is 15 seconds, jobs will be enqueued 15 seconds before the next scheduled time.
    /// </summary>
    /// <remarks>Default is max(StorageCheckInterval, 1s)</remarks>
    public TimeSpan? ScheduleLeadTime { get; set; }

    /// <summary>
    /// The interval at which the long running loops tick.
    /// </summary>
    /// <remarks>
    /// Default is 1 second. The setter is internal so that test assemblies (via InternalsVisibleTo)
    /// can reduce the tick interval for timing-sensitive tests without exposing mutation to external consumers.
    /// </remarks>
    public TimeSpan TickInterval { get; internal set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Initializes a new instance of <see cref="SchedulingOptions"/> with default values.
    /// </summary>
    /// <remarks><see cref="ScheduleLeadTime"/> defaults to the larger of <see cref="StorageCheckInterval"/> and 1 second.</remarks>
    public SchedulingOptions()
    {
        ScheduleLeadTime ??=
            StorageCheckInterval > TimeSpan.FromSeconds(1) ? StorageCheckInterval : TimeSpan.FromSeconds(1);
    }
}
