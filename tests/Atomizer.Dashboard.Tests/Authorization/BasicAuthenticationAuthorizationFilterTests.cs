using System.Net.Http.Headers;
using System.Text;
using Atomizer.Dashboard.Authorization;
using Atomizer.Dashboard.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Authorization;

public class BasicAuthenticationAuthorizationFilterTests
{
    [Fact]
    public async Task AuthorizeAsync_WhenBasicAuthCredentialsMatchOverHttps_ShouldReturnAuthorized()
    {
        var options = new DashboardOptions();
        options.Authorization.RequireBasicAuthentication("operator", "secret");
        var context = CreateContext("operator", "secret");

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenBasicAuthCredentialsDoNotMatch_ShouldReturnUnauthorizedAndChallenge()
    {
        var options = new DashboardOptions();
        options.Authorization.RequireBasicAuthentication("operator", "secret");
        var context = CreateContext("operator", "wrong");

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Unauthorized);
        context.Response.Headers.WWWAuthenticate.ToString().Should().Contain("Basic");
        context.Response.Headers.WWWAuthenticate.ToString().Should().Contain("Atomizer Dashboard");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenBasicAuthRequiresHttpsAndRequestIsHttp_ShouldReturnForbidden()
    {
        var options = new DashboardOptions();
        options.Authorization.RequireBasicAuthentication("operator", "secret");
        var context = CreateContext("operator", "secret");
        context.Request.Scheme = "http";

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Forbidden);
    }

    [Fact]
    public async Task AuthorizeAsync_WhenBasicAuthUsesAsyncValidator_ShouldReturnValidatorResult()
    {
        var options = new DashboardOptions();
        options.Authorization.RequireBasicAuthentication(
            async (_, username, password) =>
            {
                await Task.Yield();
                return username == "operator" && password == "secret";
            }
        );
        var context = CreateContext("operator", "secret");

        var result = await options.Authorization.AuthorizeAsync(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    private static HttpContext CreateContext(string username, string password)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider(validateScopes: true) };
        context.Request.Scheme = "https";
        context.Request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"))
        ).ToString();

        return context;
    }
}
