using System;

namespace Atomizer.Abstractions
{
    public interface IAtomizerJobSerializer
    {
        public string Serialize<TPayload>(TPayload payload);
        public object? Deserialize(string payload, Type payloadType);
    }
}
