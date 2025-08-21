using System;

namespace Atomizer.Models
{
    public sealed class JobKey : IEquatable<JobKey>
    {
        public JobKey(string key)
        {
            Key = key;
        }

        public string Key { get; }

        public override string ToString() => Key;

        public static bool operator ==(JobKey? left, JobKey? right) =>
            left is null && right is null || left?.Equals(right) == true;

        public static bool operator !=(JobKey? left, JobKey? right) => !(left == right);

        public static implicit operator string(JobKey jobKey) => jobKey.Key;

        public static implicit operator JobKey(string key) => new JobKey(key);

        public bool Equals(JobKey? other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            return Key == other.Key;
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is JobKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }
    }
}
