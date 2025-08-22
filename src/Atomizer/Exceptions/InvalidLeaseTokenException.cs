using System;

namespace Atomizer.Exceptions
{
    public class InvalidLeaseTokenException : ArgumentException
    {
        public InvalidLeaseTokenException(string message)
            : base(message) { }

        public InvalidLeaseTokenException(string message, Exception innerException)
            : base(message, innerException) { }

        public InvalidLeaseTokenException(string message, string paramName)
            : base(message, paramName) { }

        public InvalidLeaseTokenException(string message, string paramName, Exception innerException)
            : base(message, paramName, innerException) { }
    }
}
