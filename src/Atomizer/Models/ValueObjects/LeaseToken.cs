using System.Collections.Generic;
using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer
{
    public sealed class LeaseToken : ValueObject
    {
        public string Token { get; }

        public string InstanceId { get; }
        public QueueKey QueueKey { get; }
        public string LeaseId { get; }

        public LeaseToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidLeaseTokenException("Lease token cannot be null or empty.", nameof(token));
            }

            var parts = token.Split(":*:");

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

        protected override IEnumerable<object> GetEqualityValues()
        {
            yield return Token;
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
