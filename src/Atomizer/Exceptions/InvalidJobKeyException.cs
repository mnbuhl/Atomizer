namespace Atomizer.Exceptions;

public class InvalidJobKeyException : ArgumentException
{
    public InvalidJobKeyException(string message)
        : base(message) { }

    public InvalidJobKeyException(string message, Exception innerException)
        : base(message, innerException) { }

    public InvalidJobKeyException(string message, string paramName)
        : base(message, paramName) { }

    public InvalidJobKeyException(string message, string paramName, Exception innerException)
        : base(message, paramName, innerException) { }
}
