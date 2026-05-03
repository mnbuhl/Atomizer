namespace Atomizer.Core;

/// <summary>
/// Provides a stable per-process identity used to tag leases and error records.
/// </summary>
public class AtomizerRuntimeIdentity
{
    /// <summary>
    /// Gets the unique identifier for this process instance.
    /// <remarks>
    /// Resolved from the <c>ATOMIZER_INSTANCE_ID</c> environment variable when set;
    /// otherwise generated as <c>MachineName+{guid8}</c>.
    /// </remarks>
    /// </summary>
    public string InstanceId { get; } =
        Environment.GetEnvironmentVariable("ATOMIZER_INSTANCE_ID")
        ?? Environment.MachineName + "+" + Guid.NewGuid().ToString("N").Substring(0, 8);
}
