using System;

namespace Atomizer.Abstractions
{
    public sealed class AtomizerQueue
    {
        public static readonly AtomizerQueue Default = new AtomizerQueue("default");

        public AtomizerQueue(string name, QueueKind kind = QueueKind.Standard)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Queue name cannot be null or empty.", nameof(name));
            }

            Name = name;
            Kind = kind;
        }

        public string Name { get; set; }
        public QueueKind Kind { get; set; }

        public static implicit operator string(AtomizerQueue queue) => queue.Name;
    }

    public enum QueueKind
    {
        Standard,
        Fifo,
    }
}
