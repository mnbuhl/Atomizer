using Atomizer.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Storage
{
    public static class AtomizerOptionsExtensions
    {
        public static AtomizerOptions UseInMemoryStorage(this AtomizerOptions options)
        {
            options.JobStorageFactory = _ => new InMemoryJobStorage();
            options.JobStorageLifetime = ServiceLifetime.Singleton;
            return options;
        }
    }
}
