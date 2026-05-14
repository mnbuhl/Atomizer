using System.Net;

namespace Atomizer.Dashboard.Tests.Endpoints;

public class AuthorizationTests
{
    private static readonly string[] ApiRoutes =
    [
        "/atomizer/api/jobs",
        "/atomizer/api/schedules",
        "/atomizer/api/queues/stats",
        "/atomizer/api/servers",
    ];

    [Fact]
    public async Task AllEndpoints_WhenAuthFilterRejectsForbidden_ShouldReturn403()
    {
        using var host = new DenyAllTestHost();
        var client = host.CreateClient();

        foreach (var route in ApiRoutes)
        {
            var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"route {route} should return 403");
        }
    }

    [Fact]
    public async Task AllEndpoints_WhenAuthFilterRejectsUnauthorized_ShouldReturn401()
    {
        using var host = new UnauthorizedTestHost();
        var client = host.CreateClient();

        foreach (var route in ApiRoutes)
        {
            var response = await client.GetAsync(route, TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"route {route} should return 401");
        }
    }

    [Fact]
    public async Task GetJobById_WhenAuthFilterRejectsForbidden_ShouldReturn403()
    {
        using var host = new DenyAllTestHost();
        var client = host.CreateClient();

        var response = await client.GetAsync(
            $"/atomizer/api/jobs/{Guid.NewGuid()}",
            TestContext.Current.CancellationToken
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Index_WhenClientRequestHeaderConfigured_ShouldRenderApiRequestHeaderConfiguration()
    {
        using var host = new ClientRequestHeaderTestHost();
        var client = host.CreateClient();

        var html = await client.GetStringAsync("/atomizer", TestContext.Current.CancellationToken);

        html.Should().Contain("data-api-request-headers=");
        html.Should().Contain("X-Atomizer-Dashboard-Request");
        html.Should().Contain("test-token");
    }
}
