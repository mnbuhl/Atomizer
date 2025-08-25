namespace Atomizer.Exceptions;

public class InvalidRetryStrategyException : ArgumentException
{
    public InvalidRetryStrategyException(string message)
        : base(message) { }

    public InvalidRetryStrategyException(string message, Exception innerException)
        : base(message, innerException) { }

    public InvalidRetryStrategyException(string message, string paramName)
        : base(message, paramName) { }

    public InvalidRetryStrategyException(string message, string paramName, Exception innerException)
        : base(message, paramName, innerException) { }
}
