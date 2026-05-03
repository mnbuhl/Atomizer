namespace Atomizer.Exceptions;

/// <summary>
/// Thrown when the job handler for a payload type cannot be resolved from the service container.
/// </summary>
public class JobResolverException : Exception
{
    /// <summary>
    /// Initializes a new instance with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public JobResolverException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public JobResolverException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance for the specified payload type, embedding the type name in the message.
    /// </summary>
    /// <param name="message">Additional detail about the resolution failure.</param>
    /// <param name="payloadType">The payload type whose handler could not be resolved.</param>
    public JobResolverException(string message, Type payloadType)
        : base($"Failed to resolve job type for payload '{payloadType.FullName}': {message}") { }
}
