using System;
using System.Linq;

namespace Atomizer.Core
{
    public class AtomizerRuntimeIdentity
    {
        public string InstanceId { get; } =
            Environment.GetEnvironmentVariable("ATOMIZER_INSTANCE_ID")
            ?? Environment.MachineName + "+" + Guid.NewGuid().ToString("N").Take(8);
    }
}
