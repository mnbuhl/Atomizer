using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Identifies a recurring job schedule by name. Must be unique within the system and at most 255 characters.
/// </summary>
public sealed class JobKey : ValueObject
{
    /// <summary>
    /// Initializes a new <see cref="JobKey"/> with the specified key string.
    /// </summary>
    /// <param name="key">The job key string. Must be non-empty and at most 255 characters.</param>
    public JobKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidJobKeyException("Job key cannot be null or empty.", nameof(key));
        }

        if (key.Length > 255)
        {
            throw new InvalidJobKeyException("Job key cannot exceed 255 characters.", nameof(key));
        }

        Key = key;
    }

    /// <summary>
    /// Gets the raw key string.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Returns the raw key string.
    /// </summary>
    /// <returns>The key string.</returns>
    public override string ToString() => Key;

    /// <summary>
    /// Implicitly converts a <see cref="JobKey"/> to its underlying string.
    /// </summary>
    /// <param name="jobKey">The job key to convert.</param>
    /// <returns>The underlying key string.</returns>
    public static implicit operator string(JobKey jobKey) => jobKey.Key;

    /// <summary>
    /// Implicitly converts a string to a <see cref="JobKey"/>.
    /// </summary>
    /// <param name="key">The key string to wrap.</param>
    /// <returns>A new <see cref="JobKey"/> instance.</returns>
    public static implicit operator JobKey(string key) => new JobKey(key);

    /// <summary>
    /// Returns the equality components used to compare two <see cref="JobKey"/> instances.
    /// </summary>
    /// <returns>An enumerable containing the key string.</returns>
    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return Key;
    }
}
