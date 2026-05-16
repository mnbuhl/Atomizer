using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Configuration;

public class DashboardEndpointRouteExtensionsTests : IClassFixture<DashboardTestHost>
{
    private readonly DashboardTestHost _host;

    public DashboardEndpointRouteExtensionsTests(DashboardTestHost host)
    {
        _host = host;
    }

    [Fact]
    public void MapAtomizerDashboard_WhenEndpointApiExplorerIsRegistered_ShouldExcludeDashboardEndpoints()
    {
        _host.CreateClient();

        var descriptions = _host.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();

        var paths = descriptions
            .ApiDescriptionGroups.Items.SelectMany(group => group.Items)
            .Select(description => description.RelativePath)
            .ToList();

        paths.Should().NotContain(path => path != null && (path == "atomizer" || path.StartsWith("atomizer/")));
    }
}
