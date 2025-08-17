using System;

namespace Atomizer.Hosting
{
    public interface IAtomizerClock
    {
        DateTimeOffset UtcNow { get; }
        DateTimeOffset MinValue { get; }
        DateTimeOffset MaxValue { get; }
    }

    internal sealed class AtomizerClock : IAtomizerClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public DateTimeOffset MinValue => DateTimeOffset.MinValue;
        public DateTimeOffset MaxValue => DateTimeOffset.MaxValue;
    }
}
