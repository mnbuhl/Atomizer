using Atomizer.Dashboard.Abstractions;
using Atomizer.Dashboard.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Integration;

/// <summary>
/// xUnit <see cref="IAsyncLifetime"/> fixture that builds and starts a minimal ASP.NET Core
/// test host with Atomizer (InMemory storage) and the Atomizer Dashboard registered and mapped.
/// One instance is created per test class that declares
/// <c>IClassFixture&lt;DashboardTestHostFixture&gt;</c>.
/// </summary>
/// <remarks>
/// The fixture uses <see cref="TestServer"/> so no real TCP port is opened; all HTTP
/// traffic is in-process. Call <see cref="CreateClient"/> to obtain an
/// <see cref="HttpClient"/> that routes requests through the test server.
/// </remarks>
public sealed class DashboardTestHostFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private TestServer? _testServer;

    /// <summary>
    /// The in-process <see cref="TestServer"/> started by this fixture.
    /// Available after <see cref="InitializeAsync"/> completes.
    /// </summary>
    public TestServer TestServer =>
        _testServer ?? throw new InvalidOperationException("Test host has not been initialized.");

    /// <summary>
    /// The root <see cref="IServiceProvider"/> of the test host.
    /// Available after <see cref="InitializeAsync"/> completes.
    /// </summary>
    public IServiceProvider Services =>
        _app?.Services ?? throw new InvalidOperationException("Test host has not been initialized.");

    /// <summary>
    /// Creates a pre-configured <see cref="HttpClient"/> that sends requests directly to
    /// the in-process <see cref="TestServer"/> without opening a TCP connection.
    /// </summary>
    /// <returns>A new <see cref="HttpClient"/> bound to the test server.</returns>
    public HttpClient CreateClient() => TestServer.CreateClient();

    /// <summary>
    /// Builds a minimal <see cref="WebApplication"/> with:
    /// <list type="bullet">
    ///   <item>Atomizer services backed by the InMemory storage backend.</item>
    ///   <item>Atomizer Dashboard services (<c>AddAtomizerDashboard</c>).</item>
    ///   <item>Antiforgery middleware and mapped dashboard endpoints
    ///   (<c>MapAtomizerDashboard</c>).</item>
    /// </list>
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        // Set ApplicationName to the Atomizer.Dashboard assembly so that
        // UseStaticWebAssets() resolves the correct manifest file
        // (Atomizer.Dashboard.staticwebassets.runtime.json) that is copied to the
        // test output directory.  Without this the default entry-assembly name
        // (Atomizer.Dashboard.Tests) is used and no matching manifest is found,
        // causing /_content/... requests to return 404.
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(DashboardOptions).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
            }
        );

        // Use the in-process TestServer instead of Kestrel so no port is opened.
        builder.WebHost.UseTestServer();

        // Register Atomizer with InMemory storage.
        builder.Services.AddAtomizer(options =>
        {
            options.UseInMemoryStorage();
        });

        // Register Atomizer Dashboard services (Blazor Server + dashboard services).
        builder.Services.AddAtomizerDashboard();

        _app = builder.Build();

        // Serve static files (CSS, JS) — including /_content/Atomizer.Dashboard/* assets.
        _app.UseStaticFiles();

        // Required by Blazor Server's antiforgery validation.
        _app.UseAntiforgery();

        // Map the dashboard at the default /atomizer path.
        _app.MapAtomizerDashboard();

        await _app.StartAsync();

        _testServer = _app.GetTestServer();
    }

    /// <summary>
    /// Stops and disposes the test host, releasing all resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
