using System;

namespace Atomizer.Hosting
{
    public class AtomizerRuntimeIdentity
    {
        public string InstanceId { get; } =
            Environment.GetEnvironmentVariable("ATOMIZER_INSTANCE_ID") ?? Guid.NewGuid().ToString("N");
    }
}
