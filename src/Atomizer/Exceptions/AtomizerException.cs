using System;

namespace Atomizer.Exceptions
{
    public class AtomizerException : Exception
    {
        public string? Code { get; set; }

        protected AtomizerException(string message, string? code = null, Exception? inner = null)
            : base(message, inner) { }
    }
}
