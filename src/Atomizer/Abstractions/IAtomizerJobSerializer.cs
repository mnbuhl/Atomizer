namespace Atomizer.Abstractions;

/// <summary>
/// Serializes and deserializes job payloads to and from their string representation.
/// </summary>
public interface IAtomizerJobSerializer
{
    /// <summary>
    /// Serializes the specified payload to a string.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload to serialize.</typeparam>
    /// <param name="payload">The payload to serialize.</param>
    /// <returns>The serialized string representation of the payload.</returns>
    public string Serialize<TPayload>(TPayload payload);

    /// <summary>
    /// Deserializes the specified string to an object of the given type.
    /// </summary>
    /// <param name="payload">The serialized payload string.</param>
    /// <param name="payloadType">The target type to deserialize into.</param>
    /// <returns>The deserialized object, or <see langword="null"/> if deserialization produces no value.</returns>
    public object? Deserialize(string payload, Type payloadType);
}
