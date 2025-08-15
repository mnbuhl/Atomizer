using System;

namespace Atomizer.Configuration
{
    public class AtomizerProcessingOptions
    {
        public TimeSpan? StartupDelay { get; set; }
        public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
