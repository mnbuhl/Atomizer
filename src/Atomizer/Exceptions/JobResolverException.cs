namespace Atomizer.Exceptions;

/// <summary>
/// Thrown when the job handler type for a given payload type cannot be resolved.
/// </summary>
public class JobResolverException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="JobResolverException"/> with the specified message.
    /// </summary>
    /// <param name="message">A message describing the resolution failure.</param>
    public JobResolverException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new <see cref="JobResolverException"/> with the specified message and inner exception.
    /// </summary>
    /// <param name="message">A message describing the resolution failure.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public JobResolverException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new <see cref="JobResolverException"/> that includes the unresolvable payload type in the message.
    /// </summary>
    /// <param name="message">A message describing the resolution failure.</param>
    /// <param name="payloadType">The payload type for which no handler could be found.</param>
    public JobResolverException(string message, Type payloadType)
        : base($"Failed to resolve job type for payload '{payloadType.FullName}': {message}") { }
}
