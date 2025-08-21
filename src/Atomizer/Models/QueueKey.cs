using System;

namespace Atomizer.Models
{
    public sealed class QueueKey : IEquatable<QueueKey>
    {
        public static readonly QueueKey Default = new QueueKey("default");
        internal static readonly QueueKey Scheduler = new QueueKey("scheduler");

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

        public override string ToString() => Key;

        public override int GetHashCode() => Key.GetHashCode();

        public override bool Equals(object? obj) => obj is QueueKey other && Equals(other);

        public bool Equals(QueueKey? other) => string.Equals(Key, other?.Key, StringComparison.OrdinalIgnoreCase);

        public static bool operator ==(QueueKey? left, QueueKey? right) =>
            left is null && right is null || left?.Equals(right) == true;

        public static bool operator !=(QueueKey? left, QueueKey? right) => !(left == right);
    }
}
