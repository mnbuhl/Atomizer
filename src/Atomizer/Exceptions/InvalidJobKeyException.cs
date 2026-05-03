namespace Atomizer.Exceptions;

/// <summary>
/// Thrown when a <see cref="Atomizer.JobKey"/> value is invalid.
/// </summary>
public class InvalidJobKeyException : ArgumentException
{
    /// <summary>
    /// Initializes a new <see cref="InvalidJobKeyException"/> with the specified message.
    /// </summary>
    /// <param name="message">A message describing the invalid job key.</param>
    public InvalidJobKeyException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new <see cref="InvalidJobKeyException"/> with the specified message and inner exception.
    /// </summary>
    /// <param name="message">A message describing the invalid job key.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public InvalidJobKeyException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new <see cref="InvalidJobKeyException"/> with the specified message and parameter name.
    /// </summary>
    /// <param name="message">A message describing the invalid job key.</param>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    public InvalidJobKeyException(string message, string paramName)
        : base(message, paramName) { }

    /// <summary>
    /// Initializes a new <see cref="InvalidJobKeyException"/> with the specified message, parameter name, and inner exception.
    /// </summary>
    /// <param name="message">A message describing the invalid job key.</param>
    /// <param name="paramName">The name of the parameter that caused the exception.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public InvalidJobKeyException(string message, string paramName, Exception innerException)
        : base(message, paramName, innerException) { }
}
