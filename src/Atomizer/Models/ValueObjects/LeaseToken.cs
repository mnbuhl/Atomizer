using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Represents a distributed lease token in the format <c>InstanceId:*:QueueKey:*:LeaseId</c>.
/// </summary>
public sealed class LeaseToken : ValueObject
{
    /// <summary>
    /// Gets the full raw token string.
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// Gets the instance identifier component of the token.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets the queue key component of the token.
    /// </summary>
    public QueueKey QueueKey { get; }

    /// <summary>
    /// Gets the lease identifier component of the token.
    /// </summary>
    public string LeaseId { get; }

    /// <summary>
    /// Initializes a new <see cref="LeaseToken"/> by parsing the specified token string.
    /// </summary>
    /// <param name="token">The raw token string in the format <c>InstanceId:*:QueueKey:*:LeaseId</c>.</param>
    public LeaseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidLeaseTokenException("Lease token cannot be null or empty.", nameof(token));
        }

        var parts = token.Split(new[] { ":*:" }, StringSplitOptions.None);

        if (parts.Length != 3)
        {
            throw new InvalidLeaseTokenException(
                "Invalid lease token format. Expected format: 'InstanceId:*:QueueKey:*:LeaseId'.",
                nameof(token)
            );
        }

        InstanceId = parts.Length > 0 ? parts[0] : string.Empty;
        QueueKey = parts.Length > 1 ? new QueueKey(parts[1]) : QueueKey.Default;
        LeaseId = parts.Length > 2 ? parts[2] : string.Empty;

        Token = token;
    }

    /// <summary>
    /// Returns the equality components used to compare two <see cref="LeaseToken"/> instances.
    /// </summary>
    /// <returns>An enumerable containing the raw token string.</returns>
    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return Token;
    }

    /// <summary>
    /// Returns the raw token string.
    /// </summary>
    /// <returns>The token string.</returns>
    public override string ToString()
    {
        return Token;
    }

    /// <summary>
    /// Implicitly converts a <see cref="LeaseToken"/> to its underlying string.
    /// </summary>
    /// <param name="leaseToken">The lease token to convert.</param>
    /// <returns>The underlying token string.</returns>
    public static implicit operator string(LeaseToken leaseToken)
    {
        return leaseToken.Token;
    }

    /// <summary>
    /// Implicitly converts a string to a <see cref="LeaseToken"/>.
    /// </summary>
    /// <param name="token">The token string to wrap.</param>
    /// <returns>A new <see cref="LeaseToken"/> instance.</returns>
    public static implicit operator LeaseToken(string token)
    {
        return new LeaseToken(token);
    }
}
