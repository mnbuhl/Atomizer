using System.Collections.Generic;
using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer
{
    public sealed class QueueKey : ValueObject
    {
        public static readonly QueueKey Default = new QueueKey("default");
        internal static readonly QueueKey Scheduler = new QueueKey("scheduler");

        public QueueKey(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new InvalidQueueKeyException("Queue name cannot be null or empty.", nameof(key));
            }

            if (key.Length > 100)
            {
                throw new InvalidQueueKeyException("Queue name cannot exceed 100 characters.", nameof(key));
            }

            Key = key;
        }

        public string Key { get; }

        public static implicit operator string(QueueKey queueKey) => queueKey.Key;

        public static implicit operator QueueKey(string name) => new QueueKey(name);

        public override string ToString() => Key;

        protected override IEnumerable<object> GetEqualityValues()
        {
            yield return Key;
        }
    }
}
