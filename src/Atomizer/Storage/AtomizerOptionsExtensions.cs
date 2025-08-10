using System;
using Atomizer.Configuration;

namespace Atomizer.Storage
{
    public static class AtomizerOptionsExtensions
    {
        public static AtomizerOptions UseInMemoryStorage(
            this AtomizerOptions options,
            Action<InMemoryJobStorageOptions>? configure = null
        )
        {
            var inMemoryOptions = new InMemoryJobStorageOptions();
            configure?.Invoke(inMemoryOptions);
            options.JobStorageOptions = new JobStorageOptions(_ => new InMemoryJobStorage(inMemoryOptions));
            return options;
        }
    }
}
