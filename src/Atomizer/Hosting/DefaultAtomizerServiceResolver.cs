using System;
using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Hosting
{
    public class DefaultAtomizerServiceResolver : IAtomizerServiceResolver
    {
        private readonly IServiceProvider _serviceProvider;

        public DefaultAtomizerServiceResolver(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public object Resolve(Type type)
        {
            return _serviceProvider.GetRequiredService(type);
        }

        public TService Resolve<TService>()
            where TService : notnull
        {
            return _serviceProvider.GetRequiredService<TService>();
        }

        public IAtomizerServiceScope CreateScope()
        {
            return new DefaultAtomizerServiceScope(_serviceProvider.CreateScope());
        }
    }

    public class DefaultAtomizerServiceScope : IAtomizerServiceScope
    {
        private readonly IServiceScope _scope;

        private bool _disposed;

        public DefaultAtomizerServiceScope(IServiceScope scope)
        {
            _scope = scope;
        }

        public object Resolve(Type type)
        {
            return _scope.ServiceProvider.GetRequiredService(type);
        }

        public TService Resolve<TService>()
            where TService : notnull
        {
            return _scope.ServiceProvider.GetRequiredService<TService>();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _scope.Dispose();
        }
    }
}
