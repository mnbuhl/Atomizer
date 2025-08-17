using System;

namespace Atomizer.Models
{
    public readonly struct LeaseToken : IEquatable<LeaseToken>
    {
        public string Token { get; }

        public string InstanceId => Token.Split(":*:")[0];
        public QueueKey QueueKey => Token.Split(":*:")[1];
        public string LeaseId => Token.Split(":*:")[2];

        public LeaseToken(string token)
        {
            Token = token;
        }

        public bool Equals(LeaseToken other)
        {
            return Token == other.Token;
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
