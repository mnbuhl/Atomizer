using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Core;

internal sealed class ServiceProviderServiceScopeFactory : IAtomizerServiceScopeFactory
{
    private readonly IServiceProvider _root;

    public ServiceProviderServiceScopeFactory(IServiceProvider root) => _root = root;

    public IAtomizerServiceScope CreateScope() => new ServiceProviderServiceScope(_root.CreateScope());
}

internal sealed class ServiceProviderServiceScope : IAtomizerServiceScope
{
    private readonly IServiceScope _scope;
    public IAtomizerStorage Storage { get; }
    public IAtomizerLeasingScopeFactory LeasingScopeFactory { get; }

    public ServiceProviderServiceScope(IServiceScope scope)
    {
        _scope = scope;
        Storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        LeasingScopeFactory = scope.ServiceProvider.GetRequiredService<IAtomizerLeasingScopeFactory>();
    }

    public void Dispose() => _scope.Dispose();
}
