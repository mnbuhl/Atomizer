namespace Atomizer.Exceptions;

/// <summary>
/// Thrown when a job payload cannot be serialized or deserialized.
/// </summary>
public class PayloadSerializationException : Exception
{
    /// <summary>
    /// Initializes a new <see cref="PayloadSerializationException"/> with the specified message.
    /// </summary>
    /// <param name="message">A message describing the serialization failure.</param>
    public PayloadSerializationException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new <see cref="PayloadSerializationException"/> with the specified message and inner exception.
    /// </summary>
    /// <param name="message">A message describing the serialization failure.</param>
    /// <param name="innerException">The exception that caused this error.</param>
    public PayloadSerializationException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>
    /// Initializes a new <see cref="PayloadSerializationException"/> that includes the payload type and operation in the message.
    /// </summary>
    /// <param name="message">A message describing the serialization failure.</param>
    /// <param name="payloadType">The type of the payload that failed to serialize or deserialize.</param>
    /// <param name="deserialization">When <see langword="true"/>, indicates a deserialization failure; otherwise a serialization failure.</param>
    public PayloadSerializationException(string message, Type payloadType, bool deserialization = false)
        : base(
            $"Failed to {(deserialization ? "deserialize" : "serialize")} payload of type '{payloadType.FullName}': {message}"
        ) { }
}
