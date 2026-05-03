namespace Atomizer.EntityFrameworkCore;

/// <summary>
/// Configures the Entity Framework Core storage backend for Atomizer.
/// </summary>
public class EntityFrameworkCoreJobStorageOptions
{
    /// <summary>
    /// If true, allows falling back to providers that may not be
    /// fully supported, tested or work in distributed environments (e.g. SQLite).
    /// <remarks>Default is false. See documentation for details and implications.</remarks>
    /// </summary>
    public bool AllowUnsafeProviderFallback { get; set; } = false;

    /// <summary>
    /// Maximum time to wait when acquiring a database transaction lock before giving up.
    /// </summary>
    /// <remarks>Default is 30 seconds. If acquisition times out, the polling tick is skipped and retried on the next interval.</remarks>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
