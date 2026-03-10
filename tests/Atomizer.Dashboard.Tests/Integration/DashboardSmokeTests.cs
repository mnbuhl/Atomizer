using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Integration;

/// <summary>
/// HTTP-level smoke tests that verify the Atomizer Dashboard is correctly mapped and
/// responds to requests without server errors. Tests run against an in-process
/// <see cref="TestServer"/> so no real port is opened.
/// </summary>
public sealed class DashboardSmokeTests : IClassFixture<DashboardTestHostFixture>
{
    private readonly HttpClient _client;

    public DashboardSmokeTests(DashboardTestHostFixture fixture)
    {
        _client = fixture.CreateClient();
    }

    /// <summary>
    /// GET /atomizer should return 200 OK — the Blazor Server HTML shell must be
    /// rendered successfully without an unhandled exception.
    /// </summary>
    [Fact]
    public async Task GetAtomizer_WhenDefaultPath_ShouldReturn200()
    {
        var response = await _client.GetAsync("/atomizer", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// The HTML response for /atomizer must contain the dashboard page title so
    /// that minimal rendering of the Blazor shell is confirmed.
    /// </summary>
    [Fact]
    public async Task GetAtomizer_WhenDefaultPath_ShouldContainDashboardTitle()
    {
        var response = await _client.GetAsync("/atomizer", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.Should().Contain("Atomizer Dashboard");
    }

    /// <summary>
    /// The response must carry the <c>text/html</c> content-type so browsers can render
    /// it correctly.
    /// </summary>
    [Fact]
    public async Task GetAtomizer_WhenDefaultPath_ShouldReturnHtmlContentType()
    {
        var response = await _client.GetAsync("/atomizer", TestContext.Current.CancellationToken);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }

    /// <summary>
    /// The HTML response must reference the Blazor Server hub script so that the
    /// interactive render mode is activated in the browser.
    /// </summary>
    [Fact]
    public async Task GetAtomizer_WhenDefaultPath_ShouldIncludeBlazorServerScript()
    {
        var response = await _client.GetAsync("/atomizer", TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        html.Should().Contain("blazor.server.js");
    }

    /// <summary>
    /// A sub-path of the dashboard (e.g., /atomizer/queues) must also return 200
    /// because the Blazor router handles all paths under /atomizer.
    /// </summary>
    [Fact]
    public async Task GetAtomizerSubPath_WhenQueuesRoute_ShouldReturn200()
    {
        var response = await _client.GetAsync("/atomizer/queues", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A sub-path of the dashboard (e.g., /atomizer/jobs) must also return 200.
    /// </summary>
    [Fact]
    public async Task GetAtomizerSubPath_WhenJobsRoute_ShouldReturn200()
    {
        var response = await _client.GetAsync("/atomizer/jobs", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A sub-path of the dashboard (e.g., /atomizer/schedules) must also return 200.
    /// </summary>
    [Fact]
    public async Task GetAtomizerSubPath_WhenSchedulesRoute_ShouldReturn200()
    {
        var response = await _client.GetAsync("/atomizer/schedules", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Validates that the Blazor Server framework script is served by the infrastructure.
    /// This endpoint is registered by <c>AddInteractiveServerRenderMode</c> and must be
    /// reachable for the Blazor circuit to initialise in the browser.
    /// </summary>
    [Fact]
    public async Task GetBlazorFrameworkJs_ShouldReturn200()
    {
        var response = await _client.GetAsync("/_framework/blazor.server.js", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that the dashboard middleware can be registered with a custom base path
    /// and that requests to that custom path return 200. The path-rewriting middleware
    /// added by <c>MapAtomizerDashboard</c> must run before routing; this is achieved by
    /// calling <c>app.MapAtomizerDashboard</c> first so it inserts the rewriting middleware,
    /// then calling <c>app.UseRouting()</c> explicitly so routing resolves after the rewrite.
    /// </summary>
    [Fact]
    public async Task GetCustomBasePath_WhenRegisteredWithCustomPath_ShouldReturn200()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddAtomizer(options => options.UseInMemoryStorage());
        builder.Services.AddAtomizerDashboard(options => options.BasePath = "/admin/jobs");

        await using var app = builder.Build();
        app.UseStaticFiles();

        // Register path-rewriting middleware first (inserted by MapAtomizerDashboard),
        // then UseRouting so route matching happens after the path is rewritten.
        app.MapAtomizerDashboard("/admin/jobs");
        app.UseRouting();
        app.UseAntiforgery();

        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestServer().CreateClient();
        var response = await client.GetAsync("/admin/jobs", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Verifies that <see cref="DashboardOptions.BasePath"/> is updated to the custom value
    /// passed to <c>MapAtomizerDashboard</c> so navigation links and other options-aware code
    /// use the correct prefix.
    /// </summary>
    [Fact]
    public async Task MapAtomizerDashboard_WhenCustomBasePath_ShouldUpdateDashboardOptionsBasePath()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddAtomizer(options => options.UseInMemoryStorage());
        builder.Services.AddAtomizerDashboard();

        await using var app = builder.Build();
        app.UseStaticFiles();
        app.MapAtomizerDashboard("/my-dashboard");
        app.UseRouting();
        app.UseAntiforgery();

        await app.StartAsync(TestContext.Current.CancellationToken);

        var options = app.Services.GetRequiredService<DashboardOptions>();
        options.BasePath.Should().Be("/my-dashboard");
    }
}
