using System;

namespace Atomizer.Hosting
{
    public interface IAtomizerClock
    {
        DateTimeOffset UtcNow { get; }
    }

    public class AtomizerClock : IAtomizerClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }
}
