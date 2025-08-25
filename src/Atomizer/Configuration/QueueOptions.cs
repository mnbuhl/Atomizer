namespace Atomizer;

public sealed class QueueOptions
{
    /// <summary>
    /// The name of the queue.
    /// </summary>
    public QueueKey QueueKey { get; private set; }

    /// <summary>
    /// Gets or sets the batch size for processing jobs.
    /// <remarks>Default is 10, meaning that the queue will batch 10 jobs at a time.</remarks>
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Gets or sets the degree of parallelism for processing jobs.
    /// <remarks>Default is 4, meaning that the queue will process up to 4 jobs in parallel.</remarks>
    /// </summary>
    public int DegreeOfParallelism { get; set; } = 4;

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
    /// The interval at which the long running loops tick.
    /// </summary>
    public TimeSpan TickInterval { get; private set; } = TimeSpan.FromSeconds(1);

    public QueueOptions(QueueKey queueKey)
    {
        QueueKey = queueKey;
    }
}
