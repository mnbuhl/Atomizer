namespace Atomizer.EntityFrameworkCore;

/// <summary>
/// Configuration options for the Entity Framework Core job storage backend.
/// </summary>
public class EntityFrameworkCoreJobStorageOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to allow falling back to providers that may not be
    /// fully supported, tested or work in distributed environments (e.g. SQLite).
    /// <remarks>Default is false. See documentation for details and implications.</remarks>
    /// </summary>
    public bool AllowUnsafeProviderFallback { get; set; } = false;

    /// <summary>
    /// Gets or sets the maximum time to wait when acquiring a database transaction lock before giving up.
    /// <remarks>Default is 30 seconds. If acquisition times out, the polling tick is skipped and retried on the next interval.</remarks>
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
