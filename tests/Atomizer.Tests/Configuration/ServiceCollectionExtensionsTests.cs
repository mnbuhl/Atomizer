using Atomizer.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atomizer.Tests.Configuration;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAtomizerProcessing_WhenJobRetentionIsNull_ShouldNotRegisterRetentionHostedService()
    {
        var services = new ServiceCollection();

        services.AddAtomizerProcessing();

        services
            .Should()
            .NotContain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(AtomizerJobRetentionService)
            );
    }

    [Fact]
    public void AddAtomizerProcessing_WhenJobRetentionIsConfigured_ShouldRegisterRetentionHostedService()
    {
        var services = new ServiceCollection();

        services.AddAtomizerProcessing(options => options.JobRetention = TimeSpan.FromDays(7));

        services
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(AtomizerJobRetentionService)
            );
    }
}
