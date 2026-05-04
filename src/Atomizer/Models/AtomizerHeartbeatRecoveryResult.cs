namespace Atomizer;

/// <summary>
/// Describes the outcome of attempting to recover jobs held by a stale process instance.
/// </summary>
public sealed class AtomizerHeartbeatRecoveryResult
{
    /// <summary>
    /// Gets the instance identifier targeted by the recovery attempt.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets a value indicating whether this attempt claimed and removed the stale heartbeat.
    /// </summary>
    public bool Recovered { get; }

    /// <summary>
    /// Gets the number of processing jobs released by this recovery attempt.
    /// </summary>
    public int ReleasedJobCount { get; }

    /// <summary>
    /// Initializes a new <see cref="AtomizerHeartbeatRecoveryResult"/>.
    /// </summary>
    public AtomizerHeartbeatRecoveryResult(string instanceId, bool recovered, int releasedJobCount)
    {
        InstanceId = instanceId;
        Recovered = recovered;
        ReleasedJobCount = releasedJobCount;
    }

    /// <summary>
    /// Creates a no-op recovery result for a server that could not be claimed as stale.
    /// </summary>
    public static AtomizerHeartbeatRecoveryResult NotRecovered(string instanceId) => new(instanceId, false, 0);
}
