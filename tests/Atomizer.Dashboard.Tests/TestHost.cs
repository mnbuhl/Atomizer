using System.Reflection;
using Atomizer;
using Atomizer.Abstractions;
using Atomizer.Dashboard.Authorization;
using Atomizer.Dashboard.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atomizer.Dashboard.Tests;

public abstract class DashboardHostBase : WebApplicationFactory<Program>
{
    protected abstract IAtomizerDashboardAuthorizationFilter AuthFilter { get; }

    protected override IHostBuilder CreateHostBuilder() =>
        Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddAtomizer(options =>
                    {
                        options.UseInMemoryStorage();
                        options.AddQueue("test-queue", _ => { });
                    });
                    services.AddSingleton(sp =>
                        (Atomizer.Storage.InMemoryStorage)sp.GetRequiredService<IAtomizerStorage>()
                    );
                    services.AddAtomizerDashboard(ConfigureDashboard);
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapAtomizerDashboard());
                });
            });

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseContentRoot(AppContext.BaseDirectory);

    protected override IEnumerable<Assembly> GetTestAssemblies() => [];

    protected virtual void ConfigureDashboard(DashboardOptions options)
    {
        options.Authorization.Add(AuthFilter);
    }
}

public sealed class DashboardTestHost : DashboardHostBase
{
    protected override IAtomizerDashboardAuthorizationFilter AuthFilter { get; } = new AlwaysAllowAuthFilter();
}

public sealed class DenyAllTestHost : DashboardHostBase
{
    protected override IAtomizerDashboardAuthorizationFilter AuthFilter { get; } = new DenyAllAuthFilter();
}

public sealed class UnauthorizedTestHost : DashboardHostBase
{
    protected override IAtomizerDashboardAuthorizationFilter AuthFilter { get; } = new UnauthorizedAuthFilter();
}

public sealed class BasicAuthenticationTestHost : DashboardHostBase
{
    protected override IAtomizerDashboardAuthorizationFilter AuthFilter { get; } = new UnauthorizedAuthFilter();

    protected override void ConfigureDashboard(DashboardOptions options)
    {
        options.Authorization.RequireBasicAuthentication(
            "operator",
            "secret",
            basicOptions => basicOptions.RequireHttps = false
        );
    }
}

public sealed class ClientRequestHeaderTestHost : DashboardHostBase
{
    protected override IAtomizerDashboardAuthorizationFilter AuthFilter { get; } = new AlwaysAllowAuthFilter();

    protected override void ConfigureDashboard(DashboardOptions options)
    {
        base.ConfigureDashboard(options);
        options.Client.RequestHeaders.Add("X-Atomizer-Dashboard-Request", _ => "test-token");
    }
}

public sealed class DenyAllAuthFilter : IAtomizerDashboardAuthorizationFilter
{
    public DashboardAuthorizationResult Authorize(HttpContext context) => DashboardAuthorizationResult.Forbidden;
}

public sealed class UnauthorizedAuthFilter : IAtomizerDashboardAuthorizationFilter
{
    public DashboardAuthorizationResult Authorize(HttpContext context) => DashboardAuthorizationResult.Unauthorized;
}

internal sealed class AlwaysAllowAuthFilter : IAtomizerDashboardAuthorizationFilter
{
    public DashboardAuthorizationResult Authorize(HttpContext context) => DashboardAuthorizationResult.Authorized;
}
