namespace Atomizer.Exceptions;

/// <summary>
/// Thrown when the Atomizer framework is misconfigured at startup.
/// </summary>
public class InvalidAtomizerConfigurationException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="InvalidAtomizerConfigurationException"/> with the specified message.
    /// </summary>
    /// <param name="message">A message describing the configuration error.</param>
    public InvalidAtomizerConfigurationException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new <see cref="InvalidAtomizerConfigurationException"/> with the specified message and inner exception.
    /// </summary>
    /// <param name="message">A message describing the configuration error.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public InvalidAtomizerConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}
