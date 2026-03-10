using Atomizer.Dashboard.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard;

/// <summary>
/// Extension methods for mapping the Atomizer Dashboard into an ASP.NET Core
/// endpoint routing pipeline.
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>
    /// The internal base path that all dashboard Razor component
    /// <c>@page</c> route directives are compiled against.
    /// </summary>
    private const string InternalBasePath = "/atomizer";

    /// <summary>
    /// Maps the Atomizer Dashboard Blazor Server application into the routing
    /// pipeline using the base path previously supplied to
    /// <see cref="ServiceCollectionExtensions.AddAtomizerDashboard"/> (default:
    /// <c>/atomizer</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call <see cref="ServiceCollectionExtensions.AddAtomizerDashboard"/> during
    /// service registration before calling this method.
    /// </para>
    /// <para>
    /// When <see cref="DashboardOptions.AuthorizationPolicyName"/> is
    /// <see langword="null"/> (the default), no authorization checks are applied
    /// and the dashboard is publicly accessible. Set the property to an ASP.NET
    /// Core authorization policy name registered in the host application to
    /// restrict access.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to configure.</param>
    /// <returns>
    /// A <see cref="RazorComponentsEndpointConventionBuilder"/> that can be used
    /// to further configure the dashboard endpoints (e.g., apply additional
    /// authorization policies or metadata).
    /// </returns>
    public static RazorComponentsEndpointConventionBuilder MapAtomizerDashboard(
        this IEndpointRouteBuilder endpoints
    )
    {
        var options = endpoints.ServiceProvider.GetRequiredService<DashboardOptions>();
        RegisterPathRewritingMiddleware(endpoints, options.BasePath);
        return MapAtomizerDashboardCore(endpoints, options);
    }

    /// <summary>
    /// Maps the Atomizer Dashboard Blazor Server application into the routing
    /// pipeline under the specified <paramref name="basePath"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Requests arriving at <paramref name="basePath"/> (and its sub-paths) are
    /// transparently rewritten to the internal <c>/atomizer</c> prefix so that the
    /// compiled Blazor <c>@page</c> route directives resolve correctly.  The
    /// rewriting is registered as an <see cref="IApplicationBuilder"/> middleware
    /// and therefore requires that <paramref name="endpoints"/> is (or wraps) a
    /// <see cref="WebApplication"/> instance — which is always the case in the
    /// standard .NET 8 minimal-hosting model.
    /// </para>
    /// <para>
    /// All dashboard navigation links are updated to use
    /// <paramref name="basePath"/> automatically via
    /// <see cref="DashboardOptions.BasePath"/>.
    /// </para>
    /// <para>
    /// Call <see cref="ServiceCollectionExtensions.AddAtomizerDashboard"/> during
    /// service registration before calling this method.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to configure.</param>
    /// <param name="basePath">
    /// The URL path prefix under which the dashboard is served, e.g.
    /// <c>/admin/atomizer</c>.  Must start with a forward slash; a trailing slash
    /// is stripped automatically.  Pass <c>/atomizer</c> to use the default path.
    /// </param>
    /// <returns>
    /// A <see cref="RazorComponentsEndpointConventionBuilder"/> that can be used
    /// to further configure the dashboard endpoints.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="basePath"/> is null, empty, whitespace, or does
    /// not start with a forward slash.
    /// </exception>
    public static RazorComponentsEndpointConventionBuilder MapAtomizerDashboard(
        this IEndpointRouteBuilder endpoints,
        string basePath
    )
    {
        if (string.IsNullOrWhiteSpace(basePath))
            throw new ArgumentException(
                "Dashboard base path must not be null or empty.",
                nameof(basePath)
            );

        if (!basePath.StartsWith('/'))
            throw new ArgumentException(
                "Dashboard base path must start with a forward slash ('/').",
                nameof(basePath)
            );

        // Normalize: strip trailing slash so "/atomizer/" and "/atomizer" both work.
        var normalized = basePath.TrimEnd('/');
        if (string.IsNullOrEmpty(normalized))
            normalized = "/";

        var options = endpoints.ServiceProvider.GetRequiredService<DashboardOptions>();
        options.BasePath = normalized;

        RegisterPathRewritingMiddleware(endpoints, normalized);

        return MapAtomizerDashboardCore(endpoints, options);
    }

    /// <summary>
    /// Maps the Atomizer Dashboard Blazor Server application into the routing
    /// pipeline, applying the supplied <paramref name="configure"/> delegate to
    /// <see cref="DashboardOptions"/> before mapping.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this overload when you need to adjust multiple options (e.g.,
    /// <see cref="DashboardOptions.BasePath"/> and
    /// <see cref="DashboardOptions.AuthorizationPolicyName"/>) in a single call.
    /// Changes made inside <paramref name="configure"/> take effect for the
    /// lifetime of the host application because <see cref="DashboardOptions"/> is
    /// registered as a singleton.
    /// </para>
    /// <para>
    /// Call <see cref="ServiceCollectionExtensions.AddAtomizerDashboard"/> during
    /// service registration before calling this method.
    /// </para>
    /// </remarks>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to configure.</param>
    /// <param name="configure">
    /// A delegate used to configure the <see cref="DashboardOptions"/> singleton
    /// that was registered by
    /// <see cref="ServiceCollectionExtensions.AddAtomizerDashboard"/>.
    /// </param>
    /// <returns>
    /// A <see cref="RazorComponentsEndpointConventionBuilder"/> that can be used
    /// to further configure the dashboard endpoints.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="configure"/> is <see langword="null"/>.
    /// </exception>
    public static RazorComponentsEndpointConventionBuilder MapAtomizerDashboard(
        this IEndpointRouteBuilder endpoints,
        Action<DashboardOptions> configure
    )
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = endpoints.ServiceProvider.GetRequiredService<DashboardOptions>();
        configure(options);

        RegisterPathRewritingMiddleware(endpoints, options.BasePath);

        return MapAtomizerDashboardCore(endpoints, options);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Registers a lightweight path-rewriting middleware that translates requests
    /// arriving at <paramref name="configuredPath"/> (and its sub-paths) to the
    /// internal <see cref="InternalBasePath"/> prefix used by the compiled Blazor
    /// <c>@page</c> route directives, so that a custom base path is transparently
    /// supported without modifying any component source files.
    /// </summary>
    /// <remarks>
    /// The middleware is a no-op and is not registered when
    /// <paramref name="configuredPath"/> already equals
    /// <see cref="InternalBasePath"/>, or when <paramref name="endpoints"/>
    /// cannot be cast to <see cref="IApplicationBuilder"/> (unusual hosting
    /// scenarios that do not use <see cref="WebApplication"/>).
    /// </remarks>
    private static void RegisterPathRewritingMiddleware(
        IEndpointRouteBuilder endpoints,
        string configuredPath
    )
    {
        if (
            string.Equals(
                configuredPath,
                InternalBasePath,
                StringComparison.OrdinalIgnoreCase
            )
            || endpoints is not IApplicationBuilder appBuilder
        )
        {
            return;
        }

        appBuilder.Use(
            async (context, next) =>
            {
                if (
                    context.Request.Path.StartsWithSegments(
                        configuredPath,
                        StringComparison.OrdinalIgnoreCase,
                        out var remaining
                    )
                )
                {
                    // Rewrite e.g. /dashboard/jobs  →  /atomizer/jobs
                    //              /dashboard        →  /atomizer
                    context.Request.Path = InternalBasePath + remaining;
                }
                else if (
                    context.Request.Path.StartsWithSegments(
                        InternalBasePath,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    // Block direct access to the internal /atomizer path when the
                    // dashboard is served under a different configured path. Without
                    // this guard the underlying Blazor routes would still respond to
                    // /atomizer even though the operator configured a different prefix.
                    context.Response.StatusCode = 404;
                    return;
                }

                await next(context);
            }
        );
    }

    /// <summary>
    /// Core mapping shared by all public overloads: registers static assets,
    /// antiforgery middleware, Blazor Server interactive components, and any
    /// configured authorization policy.
    /// </summary>
    private static RazorComponentsEndpointConventionBuilder MapAtomizerDashboardCore(
        IEndpointRouteBuilder endpoints,
        DashboardOptions options
    )
    {
#if NET9_0_OR_GREATER
        endpoints.MapStaticAssets();
#endif

        if (endpoints is IApplicationBuilder appBuilder)
        {
#if !NET9_0_OR_GREATER
            appBuilder.UseStaticFiles();
#endif
            appBuilder.UseAntiforgery();
        }

        var builder = endpoints
            .MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Authorization hook — no-op when AuthorizationPolicyName is null (the
        // default), which means the dashboard is publicly accessible without any
        // auth checks.  To restrict access, set DashboardOptions.AuthorizationPolicyName
        // to the name of a policy already registered via
        // services.AddAuthorization(o => o.AddPolicy(...)).
        if (!string.IsNullOrWhiteSpace(options.AuthorizationPolicyName))
        {
            builder.RequireAuthorization(options.AuthorizationPolicyName);
        }

        return builder;
    }
}
