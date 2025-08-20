using System;

namespace Atomizer.Models
{
    public sealed class LeaseToken : IEquatable<LeaseToken>
    {
        public string Token { get; }

        public string InstanceId { get; }
        public QueueKey QueueKey { get; }
        public string LeaseId { get; }

        public LeaseToken(string token)
        {
            Token = token;

            var parts = token.Split(":*:");
            InstanceId = parts.Length > 0 ? parts[0] : string.Empty;
            QueueKey = parts.Length > 1 ? new QueueKey(parts[1]) : QueueKey.Default;
            LeaseId = parts.Length > 2 ? parts[2] : string.Empty;
        }

        public bool Equals(LeaseToken? other)
        {
            return Token == other?.Token;
        }

        public override bool Equals(object? obj)
        {
            return obj is LeaseToken other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Token.GetHashCode();
        }

        public static bool operator ==(LeaseToken left, LeaseToken right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(LeaseToken left, LeaseToken right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return Token;
        }

        public static implicit operator string(LeaseToken leaseToken)
        {
            return leaseToken.Token;
        }

        public static implicit operator LeaseToken(string token)
        {
            return new LeaseToken(token);
        }
    }
}
