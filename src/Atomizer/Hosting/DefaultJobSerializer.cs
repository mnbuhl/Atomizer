using System;
using System.Text.Json;
using Atomizer.Abstractions;
using Atomizer.Exceptions;
using Microsoft.Extensions.Logging;

namespace Atomizer.Hosting
{
    internal sealed class DefaultJobSerializer : IAtomizerJobSerializer
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        private readonly ILogger<DefaultJobSerializer> _logger;

        public DefaultJobSerializer(ILogger<DefaultJobSerializer> logger)
        {
            _logger = logger;
        }

        public string Serialize<TPayload>(TPayload payload)
        {
            try
            {
                return JsonSerializer.Serialize(payload, Options);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to serialize payload of type '{PayloadType}' to JSON: {Message}",
                    typeof(TPayload).FullName,
                    ex.Message
                );
                throw new PayloadSerializationException(
                    $"Failed to serialize payload of type '{typeof(TPayload).FullName}': {ex.Message}",
                    typeof(TPayload),
                    true
                );
            }
        }

        public object Deserialize(string payload, Type payloadType)
        {
            if (string.IsNullOrEmpty(payload))
            {
                throw new PayloadSerializationException("Payload cannot be null or empty.", payloadType, true);
            }

            object? deserialized;

            try
            {
                deserialized = JsonSerializer.Deserialize(payload, payloadType, Options);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to deserialize payload of type '{PayloadType}' from JSON: {Message}",
                    payloadType.FullName,
                    ex.Message
                );
                throw new PayloadSerializationException(
                    $"Failed to deserialize payload of type '{payloadType.FullName}': {ex.Message}",
                    payloadType,
                    true
                );
            }

            if (deserialized is null)
            {
                throw new PayloadSerializationException(
                    $"Failed to deserialize payload of type '{payloadType.FullName}' from JSON.",
                    payloadType,
                    true
                );
            }

            return deserialized;
        }
    }
}
