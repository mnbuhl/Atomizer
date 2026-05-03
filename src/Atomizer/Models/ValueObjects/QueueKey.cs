using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Identifies a named processing queue. Must be non-empty and at most 100 characters.
/// </summary>
public sealed class QueueKey : ValueObject
{
    /// <summary>
    /// Gets the default queue key used when no explicit queue is specified.
    /// </summary>
    public static readonly QueueKey Default = new QueueKey("default");

    internal static readonly QueueKey Scheduler = new QueueKey("scheduler");

    /// <summary>
    /// Initializes a new <see cref="QueueKey"/> with the specified key string.
    /// </summary>
    /// <param name="key">The queue name. Must be non-empty and at most 100 characters.</param>
    public QueueKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidQueueKeyException("Queue name cannot be null or empty.", nameof(key));
        }

        if (key.Length > 100)
        {
            throw new InvalidQueueKeyException("Queue name cannot exceed 100 characters.", nameof(key));
        }

        Key = key;
    }

    /// <summary>
    /// Gets the raw queue name string.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Implicitly converts a <see cref="QueueKey"/> to its underlying string.
    /// </summary>
    /// <param name="queueKey">The queue key to convert.</param>
    /// <returns>The underlying key string.</returns>
    public static implicit operator string(QueueKey queueKey) => queueKey.Key;

    /// <summary>
    /// Implicitly converts a string to a <see cref="QueueKey"/>.
    /// </summary>
    /// <param name="name">The queue name string to wrap.</param>
    /// <returns>A new <see cref="QueueKey"/> instance.</returns>
    public static implicit operator QueueKey(string name) => new QueueKey(name);

    /// <summary>
    /// Returns the raw queue name string.
    /// </summary>
    /// <returns>The key string.</returns>
    public override string ToString() => Key;

    /// <summary>
    /// Returns the equality components used to compare two <see cref="QueueKey"/> instances.
    /// </summary>
    /// <returns>An enumerable containing the key string.</returns>
    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return Key;
    }
}
