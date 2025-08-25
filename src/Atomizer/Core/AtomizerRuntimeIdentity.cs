using System;

namespace Atomizer.Core
{
    public class AtomizerRuntimeIdentity
    {
        public string InstanceId { get; } =
            Environment.GetEnvironmentVariable("ATOMIZER_INSTANCE_ID")
            ?? Environment.MachineName + "+" + Guid.NewGuid().ToString("N")[..8];
    }
}
