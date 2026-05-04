namespace Atomizer;

/// <summary>
/// Configures the Atomizer processing host services.
/// </summary>
public class AtomizerProcessingOptions
{
    /// <summary>
    /// Gets or sets the delay before the processing pipeline begins polling after host startup.
    /// <remarks>Defaults to no delay when <see langword="null"/>.</remarks>
    /// </summary>
    public TimeSpan? StartupDelay { get; set; }

    /// <summary>
    /// Gets or sets the maximum time to wait for in-flight jobs to complete during shutdown.
    /// <remarks>Default is 30 seconds.</remarks>
    /// </summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how frequently this process refreshes its active-server heartbeat.
    /// <remarks>Default is 30 seconds.</remarks>
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how old a heartbeat must be before the server is considered stale.
    /// <remarks>Default is 3 minutes.</remarks>
    /// </summary>
    public TimeSpan StaleServerTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Gets or sets how frequently stale-server recovery scans run.
    /// <remarks>Defaults to <see cref="HeartbeatInterval"/> when <see langword="null"/>.</remarks>
    /// </summary>
    public TimeSpan? StaleSweepInterval { get; set; }

    /// <summary>
    /// Gets the configured stale sweep interval, defaulting to <see cref="HeartbeatInterval"/>.
    /// </summary>
    public TimeSpan EffectiveStaleSweepInterval => StaleSweepInterval ?? HeartbeatInterval;

    /// <summary>
    /// Validates processing options used by hosted services.
    /// </summary>
    public void Validate()
    {
        if (StartupDelay != null && StartupDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StartupDelay), "Startup delay must be a non-negative TimeSpan.");
        }

        if (HeartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(HeartbeatInterval), "Heartbeat interval must be positive.");
        }

        if (StaleServerTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StaleServerTimeout), "Stale server timeout must be positive.");
        }

        if (StaleSweepInterval.HasValue && StaleSweepInterval.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(StaleSweepInterval), "Stale sweep interval must be positive.");
        }

        if (StaleServerTimeout < TimeSpan.FromTicks(HeartbeatInterval.Ticks * 6))
        {
            throw new ArgumentOutOfRangeException(
                nameof(StaleServerTimeout),
                "Stale server timeout must be at least six times the heartbeat interval."
            );
        }
    }
}
