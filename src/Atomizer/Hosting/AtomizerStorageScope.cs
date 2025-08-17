using System;
using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Hosting
{
    internal sealed class ServiceProviderStorageScopeFactory : IAtomizerStorageScopeFactory
    {
        private readonly IServiceProvider _root;

        public ServiceProviderStorageScopeFactory(IServiceProvider root) => _root = root;

        public IAtomizerStorageScope CreateScope() => new ServiceProviderStorageScope(_root.CreateScope());
    }

    internal sealed class ServiceProviderStorageScope : IAtomizerStorageScope
    {
        private readonly IServiceScope _scope;
        public IAtomizerStorage Storage { get; }

        public ServiceProviderStorageScope(IServiceScope scope)
        {
            _scope = scope;
            Storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        }

        public void Dispose() => _scope.Dispose();
    }
}
