namespace Atomizer.Storage;

/// <summary>
/// Configuration options for the in-memory job storage backend.
/// </summary>
public class InMemoryJobStorageOptions
{
    /// <summary>
    /// Gets or sets the maximum number of completed or failed jobs to retain in memory.
    /// <remarks>Default is 1000. Older terminal jobs are evicted when this limit is exceeded.</remarks>
    /// </summary>
    public int AmountOfJobsToRetainInMemory { get; set; } = 1000;
}
