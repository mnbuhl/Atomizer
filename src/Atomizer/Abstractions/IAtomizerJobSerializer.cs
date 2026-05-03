namespace Atomizer.Abstractions;

/// <summary>
/// Serializes and deserializes job payloads to and from their string representation.
/// </summary>
public interface IAtomizerJobSerializer
{
    /// <summary>
    /// Serializes the specified payload to its string representation.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload to serialize.</typeparam>
    /// <param name="payload">The payload instance to serialize.</param>
    /// <returns>The serialized string representation of the payload.</returns>
    public string Serialize<TPayload>(TPayload payload);

    /// <summary>
    /// Deserializes the specified string back to the payload object.
    /// </summary>
    /// <param name="payload">The serialized payload string.</param>
    /// <param name="payloadType">The target type to deserialize into.</param>
    /// <returns>The deserialized payload object, or <see langword="null"/> if deserialization fails.</returns>
    public object? Deserialize(string payload, Type payloadType);
}
