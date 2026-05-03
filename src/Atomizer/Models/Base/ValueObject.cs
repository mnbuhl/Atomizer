namespace Atomizer.Models.Base;

/// <summary>
/// Base class for Atomizer value objects that define equality by their component values.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>
    /// Returns the sequence of values that define equality for this value object.
    /// </summary>
    /// <returns>An enumerable of the equality-defining component values.</returns>
    protected abstract IEnumerable<object> GetEqualityValues();

    /// <summary>
    /// Determines whether the specified object is equal to this value object.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns>true if equal; otherwise false.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
        {
            return false;
        }

        var other = (ValueObject)obj;

        return GetEqualityValues().SequenceEqual(other.GetEqualityValues());
    }

    /// <inheritdoc/>
    public bool Equals(ValueObject? other)
    {
        return other is { } && EqualOperator(other);
    }

    /// <summary>
    /// Returns a hash code derived from the equality component values.
    /// </summary>
    public override int GetHashCode()
    {
        return GetEqualityValues().Select(x => x != null ? x.GetHashCode() : 0).Aggregate((x, y) => x ^ y);
    }

    private bool EqualOperator(ValueObject other)
    {
        return GetEqualityValues().SequenceEqual(other.GetEqualityValues());
    }

    /// <summary>
    /// Returns true if both value objects are equal or both are null.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>true if both are equal or both are null; otherwise false.</returns>
    public static bool operator ==(ValueObject? left, ValueObject? right)
    {
        if (left is null && right is null)
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Returns true if the two value objects are not equal.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns>true if the two value objects are not equal; otherwise false.</returns>
    public static bool operator !=(ValueObject? left, ValueObject? right)
    {
        return !(left == right);
    }
}
