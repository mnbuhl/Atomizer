using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Identifies a recurring schedule by name. Maximum length is 255 characters.
/// </summary>
public sealed class JobKey : ValueObject
{
    /// <summary>
    /// Initializes a new <see cref="JobKey"/> with the specified name.
    /// </summary>
    /// <param name="key">The schedule name. Must be non-empty and at most 255 characters.</param>
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
    /// Gets the schedule name.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// Returns the schedule name.
    /// </summary>
    public override string ToString() => Key;

    /// <summary>
    /// Implicitly converts a <see cref="JobKey"/> to its string representation.
    /// </summary>
    /// <param name="jobKey">The job key to convert.</param>
    /// <returns>The schedule name string.</returns>
    public static implicit operator string(JobKey jobKey) => jobKey.Key;

    /// <summary>
    /// Implicitly converts a string to a <see cref="JobKey"/>.
    /// </summary>
    /// <param name="key">The schedule name string to convert.</param>
    /// <returns>A new <see cref="JobKey"/> wrapping the string.</returns>
    public static implicit operator JobKey(string key) => new JobKey(key);

    /// <summary>
    /// Returns the schedule name as the sole equality component.
    /// </summary>
    /// <returns>An enumerable containing the schedule name.</returns>
    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return Key;
    }
}
