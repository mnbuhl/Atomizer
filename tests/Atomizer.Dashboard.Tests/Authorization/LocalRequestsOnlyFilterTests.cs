using System.Net;
using Atomizer.Dashboard.Authorization;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Tests.Authorization;

public class LocalRequestsOnlyFilterTests
{
    private readonly LocalRequestsOnlyAuthorizationFilter _sut = new();

    [Fact]
    public void Authorize_WhenRemoteIpIsNull_ShouldReturnAuthorized()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;

        var result = _sut.Authorize(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    [Fact]
    public void Authorize_WhenRemoteIpIsLoopbackIPv4_ShouldReturnAuthorized()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        var result = _sut.Authorize(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    [Fact]
    public void Authorize_WhenRemoteIpIsLoopbackIPv6_ShouldReturnAuthorized()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.IPv6Loopback;

        var result = _sut.Authorize(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    [Fact]
    public void Authorize_WhenRemoteIpMatchesLocalIp_ShouldReturnAuthorized()
    {
        var context = new DefaultHttpContext();
        var localIp = IPAddress.Parse("10.0.0.1");
        context.Connection.LocalIpAddress = localIp;
        context.Connection.RemoteIpAddress = localIp;

        var result = _sut.Authorize(context);

        result.Should().Be(DashboardAuthorizationResult.Authorized);
    }

    [Fact]
    public void Authorize_WhenRemoteIpIsExternal_ShouldReturnForbidden()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.1");
        context.Connection.LocalIpAddress = IPAddress.Parse("10.0.0.1");

        var result = _sut.Authorize(context);

        result.Should().Be(DashboardAuthorizationResult.Forbidden);
    }

    [Fact]
    public void Authorize_WhenRemoteIpIsPrivateButNotLocal_ShouldReturnForbidden()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
        context.Connection.LocalIpAddress = IPAddress.Parse("192.168.1.1");

        var result = _sut.Authorize(context);

        result.Should().Be(DashboardAuthorizationResult.Forbidden);
    }
}
