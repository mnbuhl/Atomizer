namespace Atomizer.Redis.Storage;

/// <summary>
/// Configures the Redis storage backend for Atomizer.
/// </summary>
public sealed class RedisJobStorageOptions
{
    /// <summary>
    /// Gets or sets the Redis key prefix used for all Atomizer Redis keys.
    /// </summary>
    public string KeyPrefix { get; set; } = "atomizer";

    /// <summary>
    /// Gets or sets the Redis database number. The default value uses the database configured on the connection.
    /// </summary>
    public int Database { get; set; } = -1;

    /// <summary>
    /// Gets or sets how long Redis locks remain valid if the owner process exits unexpectedly.
    /// </summary>
    public TimeSpan LockExpiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long mutating operations wait for an internal Redis lock before failing.
    /// </summary>
    public TimeSpan LockAcquisitionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets or sets the delay between attempts while waiting for an internal Redis lock.
    /// </summary>
    public TimeSpan LockRetryDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(KeyPrefix))
            throw new InvalidOperationException("Redis key prefix cannot be null or empty.");

        if (LockExpiry <= TimeSpan.Zero)
            throw new InvalidOperationException("Redis lock expiry must be greater than zero.");

        if (LockAcquisitionTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("Redis lock acquisition timeout must be greater than zero.");

        if (LockRetryDelay <= TimeSpan.Zero)
            throw new InvalidOperationException("Redis lock retry delay must be greater than zero.");
    }
}
