using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Identifies a batch of leased jobs held by a specific worker instance on a specific queue. Format: {InstanceId}:*:{QueueKey}:*:{LeaseId}.
/// </summary>
public sealed class LeaseToken : ValueObject
{
    /// <summary>
    /// Gets the raw token string.
    /// </summary>
    public string Token { get; }

    /// <summary>
    /// Gets the worker instance identifier component of the token.
    /// </summary>
    public string InstanceId { get; }

    /// <summary>
    /// Gets the queue key component of the token.
    /// </summary>
    public QueueKey QueueKey { get; }

    /// <summary>
    /// Gets the unique lease identifier component of the token.
    /// </summary>
    public string LeaseId { get; }

    /// <summary>
    /// Initializes a new <see cref="LeaseToken"/> by parsing the raw token string.
    /// </summary>
    /// <param name="token">The raw token string in format '{InstanceId}:*:{QueueKey}:*:{LeaseId}'.</param>
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

        InstanceId = parts[0];
        QueueKey = new QueueKey(parts[1]);
        LeaseId = parts[2];

        Token = token;
    }

    /// <summary>
    /// Returns the raw token string as the sole equality component.
    /// </summary>
    /// <returns>An enumerable containing the token string.</returns>
    protected override IEnumerable<object> GetEqualityValues()
    {
        yield return Token;
    }

    /// <summary>
    /// Returns the raw token string.
    /// </summary>
    public override string ToString()
    {
        return Token;
    }

    /// <summary>
    /// Implicitly converts a <see cref="LeaseToken"/> to its raw string representation.
    /// </summary>
    /// <param name="leaseToken">The lease token to convert.</param>
    /// <returns>The raw token string.</returns>
    public static implicit operator string(LeaseToken leaseToken)
    {
        return leaseToken.Token;
    }

    /// <summary>
    /// Implicitly converts a string to a <see cref="LeaseToken"/>.
    /// </summary>
    /// <param name="token">The raw token string to convert.</param>
    /// <returns>A new <see cref="LeaseToken"/> parsed from the string.</returns>
    public static implicit operator LeaseToken(string token)
    {
        return new LeaseToken(token);
    }
}
