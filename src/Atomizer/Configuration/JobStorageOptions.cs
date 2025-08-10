using System;
using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Configuration
{
    public class JobStorageOptions
    {
        public JobStorageOptions(
            Func<IServiceProvider, IAtomizerJobStorage> jobStorageFactory,
            ServiceLifetime jobStorageLifetime = ServiceLifetime.Singleton
        )
        {
            JobStorageFactory = jobStorageFactory;
            JobStorageLifetime = jobStorageLifetime;
        }

        /// <summary>
        /// Function to create the job storage.
        /// </summary>
        public Func<IServiceProvider, IAtomizerJobStorage> JobStorageFactory { get; set; }

        /// <summary>
        /// Gets or sets the lifetime of the job storage service.
        /// </summary>
        public ServiceLifetime JobStorageLifetime { get; set; }
    }
}
