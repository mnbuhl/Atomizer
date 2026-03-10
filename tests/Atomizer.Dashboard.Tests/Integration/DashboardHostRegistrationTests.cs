using Atomizer.Abstractions;
using Atomizer.Dashboard.Abstractions;
using Atomizer.Dashboard.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Tests.Integration;

/// <summary>
/// Verifies that <see cref="ServiceCollectionExtensions.AddAtomizerDashboard"/> correctly
/// registers all required services in the ASP.NET Core dependency-injection container when
/// the dashboard is hosted inside a minimal test host backed by InMemory storage.
/// </summary>
public sealed class DashboardHostRegistrationTests : IClassFixture<DashboardTestHostFixture>
{
    private readonly IServiceProvider _services;

    public DashboardHostRegistrationTests(DashboardTestHostFixture fixture)
    {
        _services = fixture.Services;
    }

    /// <summary>
    /// <see cref="DashboardOptions"/> must be resolvable as a singleton so that both
    /// <c>AddAtomizerDashboard</c> and <c>MapAtomizerDashboard</c> share the same instance.
    /// </summary>
    [Fact]
    public void Services_ShouldResolve_DashboardOptions()
    {
        var options = _services.GetService<DashboardOptions>();
        options.Should().NotBeNull();
    }

    /// <summary>
    /// <see cref="DashboardOptions"/> must be registered as a singleton; repeated resolutions
    /// must return the exact same instance.
    /// </summary>
    [Fact]
    public void DashboardOptions_ShouldBeSingleton()
    {
        var first = _services.GetRequiredService<DashboardOptions>();
        var second = _services.GetRequiredService<DashboardOptions>();
        first.Should().BeSameAs(second);
    }

    /// <summary>
    /// <see cref="DashboardOptions.BasePath"/> must default to <c>/atomizer</c> when no
    /// custom path is supplied to <c>AddAtomizerDashboard</c>.
    /// </summary>
    [Fact]
    public void DashboardOptions_BasePath_ShouldDefaultToAtomizer()
    {
        var options = _services.GetRequiredService<DashboardOptions>();
        options.BasePath.Should().Be("/atomizer");
    }

    /// <summary>
    /// <see cref="DashboardOptions.AuthorizationPolicyName"/> must default to
    /// <see langword="null"/> so the dashboard is publicly accessible out of the box.
    /// </summary>
    [Fact]
    public void DashboardOptions_AuthorizationPolicyName_ShouldDefaultToNull()
    {
        var options = _services.GetRequiredService<DashboardOptions>();
        options.AuthorizationPolicyName.Should().BeNull();
    }

    /// <summary>
    /// <see cref="IDashboardStorage"/> must be resolvable within a DI scope; the default
    /// implementation is <see cref="DashboardStorageAdapter"/>.
    /// </summary>
    [Fact]
    public void Services_ShouldResolve_IDashboardStorage()
    {
        using var scope = _services.CreateScope();
        var storage = scope.ServiceProvider.GetService<IDashboardStorage>();
        storage.Should().NotBeNull();
    }

    /// <summary>
    /// The <see cref="IAtomizerStorage"/> registration must implement
    /// <see cref="IAtomizerDashboardStorage"/> so the adapter can be constructed without
    /// throwing an <see cref="InvalidOperationException"/>.
    /// </summary>
    [Fact]
    public void AtomizerStorage_ShouldImplement_IAtomizerDashboardStorage()
    {
        using var scope = _services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IAtomizerStorage>();
        storage.Should().BeAssignableTo<IAtomizerDashboardStorage>();
    }

    /// <summary>
    /// <see cref="QueueStatsService"/> must be resolvable from a DI scope; it is registered
    /// as a scoped service by <c>AddAtomizerDashboard</c>.
    /// </summary>
    [Fact]
    public void Services_ShouldResolve_QueueStatsService()
    {
        using var scope = _services.CreateScope();
        var service = scope.ServiceProvider.GetService<QueueStatsService>();
        service.Should().NotBeNull();
    }

    /// <summary>
    /// <see cref="ScheduleSummaryService"/> must be resolvable from a DI scope.
    /// </summary>
    [Fact]
    public void Services_ShouldResolve_ScheduleSummaryService()
    {
        using var scope = _services.CreateScope();
        var service = scope.ServiceProvider.GetService<ScheduleSummaryService>();
        service.Should().NotBeNull();
    }

    /// <summary>
    /// <see cref="ScheduleTriggerService"/> must be resolvable from a DI scope.
    /// </summary>
    [Fact]
    public void Services_ShouldResolve_ScheduleTriggerService()
    {
        using var scope = _services.CreateScope();
        var service = scope.ServiceProvider.GetService<ScheduleTriggerService>();
        service.Should().NotBeNull();
    }

    /// <summary>
    /// <see cref="CancelJobService"/> must be resolvable from a DI scope.
    /// </summary>
    [Fact]
    public void Services_ShouldResolve_CancelJobService()
    {
        using var scope = _services.CreateScope();
        var service = scope.ServiceProvider.GetService<CancelJobService>();
        service.Should().NotBeNull();
    }

    /// <summary>
    /// The core <see cref="IAtomizerClient"/> must be resolvable; it is provided by
    /// <c>AddAtomizer()</c> and must be present for the dashboard host to be valid.
    /// </summary>
    [Fact]
    public void Services_ShouldResolve_IAtomizerClient()
    {
        var client = _services.GetService<IAtomizerClient>();
        client.Should().NotBeNull();
    }
}
