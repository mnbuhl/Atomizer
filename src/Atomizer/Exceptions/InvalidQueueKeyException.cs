using System;

namespace Atomizer.Exceptions
{
    public class InvalidQueueKeyException : ArgumentException
    {
        public InvalidQueueKeyException(string message)
            : base(message) { }

        public InvalidQueueKeyException(string message, Exception innerException)
            : base(message, innerException) { }

        public InvalidQueueKeyException(string message, string paramName)
            : base(message, paramName) { }

        public InvalidQueueKeyException(string message, string paramName, Exception innerException)
            : base(message, paramName, innerException) { }
    }
}
