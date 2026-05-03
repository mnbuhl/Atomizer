namespace Atomizer.Models.Base;

/// <summary>
/// Base class for immutable value objects with structural equality.
/// </summary>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>
    /// Returns the component values used to determine equality for this value object.
    /// </summary>
    /// <returns>An ordered sequence of equality components.</returns>
    protected abstract IEnumerable<object> GetEqualityValues();

    /// <summary>
    /// Determines whether the specified object is equal to this value object.
    /// </summary>
    /// <param name="obj">The object to compare with the current instance.</param>
    /// <returns><see langword="true"/> if the objects are equal; otherwise <see langword="false"/>.</returns>
    public override bool Equals(object? obj)
    {
        if (obj is null || obj.GetType() != GetType())
        {
            return false;
        }

        var other = (ValueObject)obj;

        return GetEqualityValues().SequenceEqual(other.GetEqualityValues());
    }

    /// <summary>
    /// Determines whether the specified <see cref="ValueObject"/> is equal to this instance.
    /// </summary>
    /// <param name="other">The value object to compare with the current instance.</param>
    /// <returns><see langword="true"/> if the value objects are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(ValueObject? other)
    {
        return other is { } && EqualOperator(other);
    }

    /// <summary>
    /// Returns a hash code computed from the equality components.
    /// </summary>
    /// <returns>A hash code for the current value object.</returns>
    public override int GetHashCode()
    {
        return GetEqualityValues().Select(x => x != null ? x.GetHashCode() : 0).Aggregate((x, y) => x ^ y);
    }

    private bool EqualOperator(ValueObject other)
    {
        return GetEqualityValues().SequenceEqual(other.GetEqualityValues());
    }

    /// <summary>
    /// Returns <see langword="true"/> if the two value objects are equal.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if equal; otherwise <see langword="false"/>.</returns>
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
    /// Returns <see langword="true"/> if the two value objects are not equal.
    /// </summary>
    /// <param name="left">The left operand.</param>
    /// <param name="right">The right operand.</param>
    /// <returns><see langword="true"/> if not equal; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(ValueObject? left, ValueObject? right)
    {
        return !(left == right);
    }
}
