namespace Atomizer.Exceptions;

/// <summary>
/// Thrown when a <see cref="Atomizer.LeaseToken"/> value is invalid or malformed.
/// </summary>
public class InvalidLeaseTokenException : ArgumentException
{
    /// <summary>
    /// Initializes a new <see cref="InvalidLeaseTokenException"/> with the specified message.
    /// </summary>
    /// <param name="message">A message describing the invalid lease token.</param>
    public InvalidLeaseTokenException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new <see cref="InvalidLeaseTokenException"/> with the specified message and inner exception.
    /// </summary>
    /// <param name="message">A message describing the invalid lease token.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public InvalidLeaseTokenException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new <see cref="InvalidLeaseTokenException"/> with the specified message and parameter name.
    /// </summary>
    /// <param name="message">A message describing the invalid lease token.</param>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    public InvalidLeaseTokenException(string message, string paramName)
        : base(message, paramName) { }

    /// <summary>
    /// Initializes a new <see cref="InvalidLeaseTokenException"/> with the specified message, parameter name, and inner exception.
    /// </summary>
    /// <param name="message">A message describing the invalid lease token.</param>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public InvalidLeaseTokenException(string message, string paramName, Exception innerException)
        : base(message, paramName, innerException) { }
}
