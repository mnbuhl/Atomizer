using System;

namespace Atomizer.Abstractions
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

            Key = key;
        }

        public string Key { get; }

        public static implicit operator string(QueueKey queueKey) => queueKey.Key;

        public static implicit operator QueueKey(string name) => new QueueKey(name);

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
