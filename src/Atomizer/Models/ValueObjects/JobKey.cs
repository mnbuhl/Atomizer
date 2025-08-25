using System.Collections.Generic;
using Atomizer.Exceptions;
using Atomizer.Models.Base;

namespace Atomizer
{
    public sealed class JobKey : ValueObject
    {
        public JobKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidJobKeyException("Job key cannot be null or empty.", nameof(key));
            }

            if (key.Length > 255)
            {
                throw new InvalidJobKeyException("Job key cannot exceed 255 characters.", nameof(key));
            }

            Key = key;
        }

        public string Key { get; }

        public override string ToString() => Key;

        public static implicit operator string(JobKey jobKey) => jobKey.Key;

        public static implicit operator JobKey(string key) => new JobKey(key);

        protected override IEnumerable<object> GetEqualityValues()
        {
            yield return Key;
        }
    }
}
