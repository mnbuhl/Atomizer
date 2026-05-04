using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Identifies a FIFO partition for ordered job processing. Maximum length is 255 characters.
/// </summary>
public sealed class PartitionKey : ValueObject
{
    /// <summary>
    /// Initializes a new <see cref="PartitionKey"/> with the specified key.
    /// </summary>
    /// <param name="key">The partition key. Must be non-empty and at most 255 characters.</param>
    public PartitionKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidPartitionKeyException("Partition key cannot be null or empty.", nameof(key));
        }

        if (key.Length > 255)
        {
            throw new InvalidPartitionKeyException("Partition key cannot exceed 255 characters.", nameof(key));
        }

        Key = key;
    }

    /// <summary>
    /// Gets the partition key string.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Returns the partition key string.
    /// </summary>
    public override string ToString() => Key;

    /// <summary>
    /// Implicitly converts a <see cref="PartitionKey"/> to its string representation.
    /// </summary>
    /// <param name="partitionKey">The partition key to convert.</param>
    /// <returns>The partition key string.</returns>
    public static implicit operator string(PartitionKey partitionKey) => partitionKey.Key;

    /// <summary>
    /// Implicitly converts a string to a <see cref="PartitionKey"/>.
    /// </summary>
    /// <param name="key">The partition key string to convert.</param>
    /// <returns>A new <see cref="PartitionKey"/> wrapping the string.</returns>
    public static implicit operator PartitionKey(string key) => new PartitionKey(key);

    /// <summary>
    /// Returns the partition key string as the sole equality component.
    /// </summary>
    /// <returns>An enumerable containing the partition key string.</returns>
    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return Key;
    }
}
