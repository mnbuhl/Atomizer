namespace Atomizer.Exceptions;

/// <summary>
/// Thrown when the Atomizer configuration is invalid or incomplete.
/// </summary>
public class InvalidAtomizerConfigurationException : Exception
{
    /// <summary>
    /// Initializes a new instance with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidAtomizerConfigurationException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public InvalidAtomizerConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}
