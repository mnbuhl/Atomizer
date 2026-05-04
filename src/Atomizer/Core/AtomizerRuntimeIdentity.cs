namespace Atomizer.Core;

/// <summary>
/// Provides a stable per-process identity used to tag leases and error records.
/// </summary>
public class AtomizerRuntimeIdentity
{
    /// <summary>
    /// Initializes a new <see cref="AtomizerRuntimeIdentity"/> using <c>ATOMIZER_INSTANCE_ID</c> or a generated value.
    /// </summary>
    public AtomizerRuntimeIdentity()
        : this(
            Environment.GetEnvironmentVariable("ATOMIZER_INSTANCE_ID")
                ?? Environment.MachineName + "+" + Guid.NewGuid().ToString("N").Substring(0, 8)
        )
    { }

    /// <summary>
    /// Initializes a new <see cref="AtomizerRuntimeIdentity"/> with the supplied process instance identifier.
    /// </summary>
    /// <param name="instanceId">The process instance identifier.</param>
    public AtomizerRuntimeIdentity(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("Atomizer instance id cannot be null or empty.", nameof(instanceId));
        }

        if (instanceId.Contains(LeaseToken.Delimiter))
        {
            throw new ArgumentException(
                $"Atomizer instance id cannot contain the lease-token delimiter '{LeaseToken.Delimiter}'.",
                nameof(instanceId)
            );
        }

        InstanceId = instanceId;
    }

    /// <summary>
    /// Gets the unique identifier for this process instance.
    /// <remarks>
    /// Resolved from the <c>ATOMIZER_INSTANCE_ID</c> environment variable when set;
    /// otherwise generated as <c>MachineName+{guid8}</c>.
    /// </remarks>
    /// </summary>
    public string InstanceId { get; }
}
