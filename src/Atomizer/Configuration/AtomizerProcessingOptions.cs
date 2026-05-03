namespace Atomizer;

/// <summary>
/// Configuration options for the Atomizer processing pipeline (queue workers and scheduler).
/// </summary>
public class AtomizerProcessingOptions
{
    /// <summary>
    /// Gets or sets an optional delay before the processing pipeline starts accepting jobs after host startup.
    /// <remarks>Default is no delay. Useful to allow dependent services to initialize first.</remarks>
    /// </summary>
    public TimeSpan? StartupDelay { get; set; }

    /// <summary>
    /// Gets or sets the maximum time the pipeline waits for in-flight jobs to complete during shutdown.
    /// <remarks>Default is 30 seconds.</remarks>
    /// </summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
