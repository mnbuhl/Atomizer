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
}
