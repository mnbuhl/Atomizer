namespace Atomizer.Exceptions;

/// <summary>
/// Thrown when a <see cref="RetryStrategy"/> is constructed with invalid parameters.
/// </summary>
public class InvalidRetryStrategyException : ArgumentException
{
    /// <summary>
    /// Initializes a new instance with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidRetryStrategyException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public InvalidRetryStrategyException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance with the specified message and parameter name.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    public InvalidRetryStrategyException(string message, string paramName)
        : base(message, paramName) { }

    /// <summary>
    /// Initializes a new instance with the specified message, parameter name, and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public InvalidRetryStrategyException(string message, string paramName, Exception innerException)
        : base(message, paramName, innerException) { }
}
