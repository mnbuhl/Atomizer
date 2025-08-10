using Atomizer.Configuration;

namespace Atomizer.Storage
{
    public static class AtomizerOptionsExtensions
    {
        public static AtomizerOptions UseInMemoryStorage(this AtomizerOptions options)
        {
            options.JobStorageOptions = new JobStorageOptions(_ => new InMemoryJobStorage());
            return options;
        }
    }
}
