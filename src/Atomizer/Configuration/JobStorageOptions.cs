using System;
using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer
{
    public class JobStorageOptions
    {
        public JobStorageOptions(
            Func<IServiceProvider, IAtomizerStorage> jobStorageFactory,
            ServiceLifetime jobStorageLifetime = ServiceLifetime.Singleton
        )
        {
            JobStorageFactory = jobStorageFactory;
            JobStorageLifetime = jobStorageLifetime;
        }

        /// <summary>
        /// Function to create the job storage.
        /// </summary>
        public Func<IServiceProvider, IAtomizerStorage> JobStorageFactory { get; set; }

        /// <summary>
        /// Gets or sets the lifetime of the job storage service.
        /// </summary>
        public ServiceLifetime JobStorageLifetime { get; set; }
    }
}
