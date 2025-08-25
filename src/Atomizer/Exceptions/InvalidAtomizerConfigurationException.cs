namespace Atomizer.Exceptions;

public class InvalidAtomizerConfigurationException : Exception
{
    public InvalidAtomizerConfigurationException(string message)
        : base(message) { }

    public InvalidAtomizerConfigurationException(string message, Exception innerException)
        : base(message, innerException) { }
}
