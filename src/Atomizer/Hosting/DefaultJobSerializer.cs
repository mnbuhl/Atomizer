using System;
using System.Text.Json;
using Atomizer.Abstractions;

namespace Atomizer.Hosting
{
    internal sealed class DefaultJobSerializer : IJobSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        public string Serialize<TPayload>(TPayload payload)
        {
            return JsonSerializer.Serialize(payload, Options);
        }

        public object? Deserialize(string payload, Type payloadType)
        {
            return JsonSerializer.Deserialize(payload, payloadType, Options);
        }
    }
}
