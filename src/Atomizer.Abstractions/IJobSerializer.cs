using System;

namespace Atomizer.Abstractions
{
    public interface IJobSerializer
    {
        public string Serialize<TPayload>(TPayload payload);
        public object? Deserialize(string payload, Type payloadType);
    }
}
