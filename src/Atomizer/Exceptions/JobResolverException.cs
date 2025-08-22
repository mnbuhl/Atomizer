using System;

namespace Atomizer.Exceptions
{
    public class JobResolverException : Exception
    {
        public JobResolverException(string message)
            : base(message) { }

        public JobResolverException(string message, Exception innerException)
            : base(message, innerException) { }

        public JobResolverException(string message, Type payloadType)
            : base($"Failed to resolve job type for payload '{payloadType.FullName}': {message}") { }
    }
}
