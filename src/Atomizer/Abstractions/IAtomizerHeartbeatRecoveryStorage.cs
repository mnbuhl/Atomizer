namespace Atomizer.Abstractions;

/// <summary>
/// Defines storage operations needed by process-level heartbeat recovery.
/// </summary>
public interface IAtomizerHeartbeatRecoveryStorage
{
    /// <summary>
    /// Validates that this storage backend can safely perform distributed heartbeat recovery.
    /// </summary>
    void ValidateHeartbeatRecoverySupport();

    /// <summary>
    /// Inserts or refreshes the current process heartbeat.
    /// </summary>
    Task UpsertHeartbeatAsync(AtomizerActiveServer server, CancellationToken cancellationToken);

    /// <summary>
    /// Returns active server records whose last heartbeat is older than <paramref name="staleBefore"/>.
    /// </summary>
    Task<IReadOnlyList<AtomizerActiveServer>> GetStaleServersAsync(
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Atomically claims a stale server and releases all processing jobs leased by that exact instance.
    /// </summary>
    Task<AtomizerHeartbeatRecoveryResult> TryRecoverStaleServerAsync(
        string instanceId,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes a server heartbeat record when the current process shuts down cleanly.
    /// </summary>
    Task RemoveHeartbeatAsync(string instanceId, CancellationToken cancellationToken);
}
