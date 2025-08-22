using System;

namespace Atomizer.Exceptions
{
    public class PayloadSerializationException : Exception
    {
        public PayloadSerializationException(string message)
            : base(message) { }

        public PayloadSerializationException(string message, Exception innerException)
            : base(message, innerException) { }

        public PayloadSerializationException(string message, Type payloadType, bool deserialization = false)
            : base(
                $"Failed to {(deserialization ? "deserialize" : "serialize")} payload of type '{payloadType.FullName}': {message}"
            ) { }
    }
}
