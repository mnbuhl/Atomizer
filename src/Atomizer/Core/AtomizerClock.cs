namespace Atomizer.Core;

/// <summary>
/// Provides an ambient clock abstraction for obtaining the current time and sentinel values.
/// </summary>
public interface IAtomizerClock
{
    /// <summary>
    /// Gets the current UTC date and time.
    /// </summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>
    /// Gets the minimum representable <see cref="DateTimeOffset"/> value.
    /// </summary>
    DateTimeOffset MinValue { get; }

    /// <summary>
    /// Gets the maximum representable <see cref="DateTimeOffset"/> value.
    /// </summary>
    DateTimeOffset MaxValue { get; }
}

internal sealed class AtomizerClock : IAtomizerClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public DateTimeOffset MinValue => DateTimeOffset.MinValue;
    public DateTimeOffset MaxValue => DateTimeOffset.MaxValue;
}
