using System.Security.Claims;
using Atomizer.Dashboard.Authorization;
using Atomizer.Dashboard.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Authorization;

public class DashboardAuthorizationOptionsTests
{
    [Fact]
    public async Task AuthorizeAsync_WhenAuthenticatedUserRequiredAndPrincipalIsAuthenticated_ShouldReturnAuthorized()
    {
        var options = new DashboardOptions();
        options.Authorization.RequireAuthenticatedUser();
        var context = CreateContext(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator")], "Test"))
        );

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenAuthenticatedUserRequiredAndPrincipalIsAnonymous_ShouldReturnUnauthorized()
    {
        var options = new DashboardOptions();
        options.Authorization.RequireAuthenticatedUser();
        var context = CreateContext(new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Unauthorized);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenRoleRequiredAndPrincipalHasRole_ShouldReturnAuthorized()
    {
        var options = new DashboardOptions();
        options.Authorization.RequireRoles("atomizer-operators");
        var context = CreateContext(
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "operator"), new Claim(ClaimTypes.Role, "atomizer-operators")],
                    "Test"
                )
            )
        );

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenRoleRequiredAndPrincipalIsAuthenticatedWithoutRole_ShouldReturnForbidden()
    {
        var options = new DashboardOptions();
        options.Authorization.RequireRoles("atomizer-operators");
        var context = CreateContext(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "operator")], "Test"))
        );

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Forbidden);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenClaimRequiredAndPrincipalHasClaimValue_ShouldReturnAuthorized()
    {
        var options = new DashboardOptions();
        options.Authorization.RequireClaim("scope", "atomizer.dashboard.read");
        var context = CreateContext(
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "operator"), new Claim("scope", "atomizer.dashboard.read")],
                    "Test"
                )
            )
        );

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenNamedPolicyMatchesPrincipal_ShouldReturnAuthorized()
    {
        var options = new DashboardOptions();
        options.Authorization.RequirePolicy("AtomizerDashboard");
        var context = CreateContext(
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, "operator"), new Claim("scope", "atomizer.dashboard.read")],
                    "Test"
                )
            ),
            services =>
                services.AddAuthorization(options =>
                    options.AddPolicy(
                        "AtomizerDashboard",
                        policy => policy.RequireClaim("scope", "atomizer.dashboard.read")
                    )
                )
        );

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenCustomFilterUsesAsyncWork_ShouldReturnFilterResult()
    {
        var options = new DashboardOptions();
        options.Authorization.Add(new AsyncAllowAuthorizationFilter());
        var context = CreateContext(new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
        context.Items.Should().ContainKey("async-filter");
    }

    private static HttpContext CreateContext(ClaimsPrincipal user, Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        if (configureServices is null)
            services.AddAuthorization();
        else
            configureServices(services);

        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(validateScopes: true),
            User = user,
        };

        return context;
    }

    private sealed class AsyncAllowAuthorizationFilter : IAtomizerDashboardAuthorizationFilter
    {
        public async ValueTask<DashboardAuthorizationResult> AuthorizeAsync(HttpContext context)
        {
            await Task.Yield();
            context.Items["async-filter"] = true;

            return DashboardAuthorizationResult.Authorized;
        }
    }
}
