namespace Atomizer.EntityFrameworkCore;

public class EntityFrameworkCoreJobStorageOptions
{
    /// <summary>
    /// If true, allows falling back to providers that may not be
    /// fully supported, tested or work in distributed environments (e.g. SQLite).
    /// <remarks>Default is false. See documentation for details and implications.</remarks>
    /// </summary>
    public bool AllowUnsafeProviderFallback { get; set; } = false;
}
