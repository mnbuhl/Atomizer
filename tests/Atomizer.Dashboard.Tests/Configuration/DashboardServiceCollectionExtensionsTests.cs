using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Configuration;

public class DashboardServiceCollectionExtensionsTests
{
    [Fact]
    public void AddAtomizerDashboard_WhenCalled_ShouldRegisterEndpointHandlers()
    {
        var services = new ServiceCollection();

        services.AddAtomizerDashboard();

        var dashboardAssembly = typeof(DashboardServiceCollectionExtensions).Assembly;
        var handlerTypeNames = new[]
        {
            "Atomizer.Dashboard.Endpoints.JobsEndpointHandler",
            "Atomizer.Dashboard.Endpoints.SchedulesEndpointHandler",
            "Atomizer.Dashboard.Endpoints.QueueStatsEndpointHandler",
            "Atomizer.Dashboard.Endpoints.ServersEndpointHandler",
            "Atomizer.Dashboard.Endpoints.StaticFilesEndpointHandler",
        };

        foreach (var handlerTypeName in handlerTypeNames)
        {
            var handlerType = dashboardAssembly.GetType(handlerTypeName);

            handlerType.Should().NotBeNull($"{handlerTypeName} should exist as a DI-resolved endpoint handler");
            services
                .Should()
                .Contain(
                    descriptor =>
                        descriptor.ServiceType == handlerType!
                        && descriptor.ImplementationType == handlerType
                        && descriptor.Lifetime == ServiceLifetime.Scoped,
                    $"{handlerTypeName} should be resolved from the request DI scope"
                );
        }
    }
}
