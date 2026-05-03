namespace Atomizer.Exceptions;

/// <summary>
/// Thrown when a job payload cannot be serialized or deserialized.
/// </summary>
public class PayloadSerializationException : Exception
{
    /// <summary>
    /// Initializes a new instance with the specified message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PayloadSerializationException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new instance with the specified message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    public PayloadSerializationException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new instance for the specified payload type, embedding the type name and direction in the message.
    /// </summary>
    /// <param name="message">Additional detail about the failure.</param>
    /// <param name="payloadType">The payload type that failed to serialize or deserialize.</param>
    /// <param name="deserialization">When true, the failure was during deserialization; otherwise serialization.</param>
    public PayloadSerializationException(string message, Type payloadType, bool deserialization = false)
        : base(
            $"Failed to {(deserialization ? "deserialize" : "serialize")} payload of type '{payloadType.FullName}': {message}"
        ) { }
}
