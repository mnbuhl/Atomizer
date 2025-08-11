using System;

namespace Atomizer.Models
{
    public sealed class QueueKey
    {
        public static readonly QueueKey Default = new QueueKey("default");

        public QueueKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Queue name cannot be null or empty.", nameof(key));
            }

            if (key.Length > 100)
            {
                throw new ArgumentException("Queue name cannot exceed 100 characters.", nameof(key));
            }

            Key = key;
        }

        public string Key { get; }

        public static implicit operator string(QueueKey queueKey) => queueKey.Key;

        public static implicit operator QueueKey(string name) => new QueueKey(name);

        public static bool operator ==(QueueKey? left, QueueKey? right)
        {
            if (left is null && right is null)
                return true;
            if (left is null || right is null)
                return false;
            return string.Equals(left.Key, right.Key, StringComparison.OrdinalIgnoreCase);
        }

        public static bool operator !=(QueueKey? left, QueueKey? right) => !(left == right);

        public override string ToString() => Key;

        public override bool Equals(object? obj)
        {
            if (obj is QueueKey other)
            {
                return string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public override int GetHashCode()
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(Key);
        }
    }
}
