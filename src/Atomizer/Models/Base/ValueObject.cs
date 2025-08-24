using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomizer.Models.Base
{
    public abstract class ValueObject : IEquatable<ValueObject>
    {
        protected abstract IEnumerable<object> GetEqualityValues();

        public override bool Equals(object? obj)
        {
            if (obj is null || obj.GetType() != GetType())
            {
                return false;
            }

            var other = (ValueObject)obj;

            return GetEqualityValues().SequenceEqual(other.GetEqualityValues());
        }

        public bool Equals(ValueObject? other)
        {
            return other is { } && EqualOperator(other);
        }

        public override int GetHashCode()
        {
            return GetEqualityValues().Select(x => x != null ? x.GetHashCode() : 0).Aggregate((x, y) => x ^ y);
        }

        private bool EqualOperator(ValueObject other)
        {
            return GetEqualityValues().SequenceEqual(other.GetEqualityValues());
        }

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

        public static bool operator !=(ValueObject? left, ValueObject? right)
        {
            return !(left == right);
        }
    }
}
