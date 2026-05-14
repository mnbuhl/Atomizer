using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard.Authorization;

/// <summary>
/// Configures authorization requirements for Atomizer Dashboard requests.
/// </summary>
/// <remarks>
/// Dashboard authorization filters are evaluated as alternatives. The first filter that returns
/// <see cref="DashboardAuthorizationResult.Authorized"/> allows the request.
/// </remarks>
public sealed class DashboardAuthorizationOptions
{
    private readonly List<Func<HttpContext, ValueTask<DashboardAuthorizationResult>>> _filters = [];

    /// <summary>
    /// Gets the number of configured dashboard authorization filters.
    /// </summary>
    public int Count => _filters.Count;

    /// <summary>
    /// Adds a custom dashboard authorization filter.
    /// </summary>
    /// <param name="filter">The filter to evaluate for dashboard requests.</param>
    public void Add(IAtomizerDashboardAuthorizationFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filters.Add(filter.AuthorizeAsync);
    }

    /// <summary>
    /// Adds a custom dashboard authorization delegate.
    /// </summary>
    /// <param name="authorize">The delegate to evaluate for dashboard requests.</param>
    public void Add(Func<HttpContext, DashboardAuthorizationResult> authorize)
    {
        ArgumentNullException.ThrowIfNull(authorize);

        _filters.Add(context => ValueTask.FromResult(authorize(context)));
    }

    /// <summary>
    /// Adds an asynchronous custom dashboard authorization delegate.
    /// </summary>
    /// <param name="authorize">The delegate to evaluate for dashboard requests.</param>
    public void Add(Func<HttpContext, ValueTask<DashboardAuthorizationResult>> authorize)
    {
        ArgumentNullException.ThrowIfNull(authorize);

        _filters.Add(authorize);
    }

    /// <summary>
    /// Requires the current ASP.NET Core user to be authenticated.
    /// </summary>
    public void RequireAuthenticatedUser()
    {
        RequireAuthorization(policy => policy.RequireAuthenticatedUser());
    }

    /// <summary>
    /// Requires the current ASP.NET Core user to have at least one of the specified roles.
    /// </summary>
    /// <param name="roles">The allowed roles.</param>
    public void RequireRoles(params string[] roles)
    {
        if (roles is null || roles.Length == 0)
            throw new ArgumentException("At least one role is required.", nameof(roles));

        RequireAuthorization(policy => policy.RequireAuthenticatedUser().RequireRole(roles));
    }

    /// <summary>
    /// Requires the current ASP.NET Core user to have the specified claim.
    /// </summary>
    /// <param name="claimType">The required claim type.</param>
    /// <param name="allowedValues">Optional allowed claim values.</param>
    public void RequireClaim(string claimType, params string[] allowedValues)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            throw new ArgumentException("A claim type is required.", nameof(claimType));

        allowedValues ??= [];
        RequireAuthorization(policy =>
        {
            policy.RequireAuthenticatedUser();
            if (allowedValues.Length == 0)
            {
                policy.RequireClaim(claimType);
                return;
            }

            policy.RequireClaim(claimType, allowedValues);
        });
    }

    /// <summary>
    /// Requires the current ASP.NET Core user to satisfy the named authorization policy.
    /// </summary>
    /// <param name="policyName">The ASP.NET Core authorization policy name.</param>
    public void RequirePolicy(string policyName)
    {
        if (string.IsNullOrWhiteSpace(policyName))
            throw new ArgumentException("A policy name is required.", nameof(policyName));

        _filters.Add(context => AuthorizePolicyAsync(context, policyName, policy: null));
    }

    /// <summary>
    /// Requires the current ASP.NET Core user to satisfy a dashboard-specific authorization policy.
    /// </summary>
    /// <param name="configurePolicy">Configures the authorization policy.</param>
    public void RequireAuthorization(Action<AuthorizationPolicyBuilder> configurePolicy)
    {
        ArgumentNullException.ThrowIfNull(configurePolicy);

        var builder = new AuthorizationPolicyBuilder();
        configurePolicy(builder);
        var policy = builder.Build();

        _filters.Add(context => AuthorizePolicyAsync(context, policyName: null, policy));
    }

    /// <summary>
    /// Requires HTTP Basic authentication with fixed credentials.
    /// </summary>
    /// <param name="username">The required username.</param>
    /// <param name="password">The required password.</param>
    /// <param name="configure">Optional Basic authentication configuration.</param>
    public void RequireBasicAuthentication(
        string username,
        string password,
        Action<DashboardBasicAuthenticationOptions>? configure = null
    )
    {
        Add(new BasicAuthenticationDashboardAuthorizationFilter(username, password, configure));
    }

    /// <summary>
    /// Requires HTTP Basic authentication with custom asynchronous credential validation.
    /// </summary>
    /// <param name="validateCredentials">Validates the current request, username, and password.</param>
    /// <param name="configure">Optional Basic authentication configuration.</param>
    public void RequireBasicAuthentication(
        Func<HttpContext, string, string, ValueTask<bool>> validateCredentials,
        Action<DashboardBasicAuthenticationOptions>? configure = null
    )
    {
        Add(new BasicAuthenticationDashboardAuthorizationFilter(validateCredentials, configure));
    }

    internal async ValueTask<DashboardAuthorizationResult> AuthorizeAsync(HttpContext context)
    {
        var denial = DashboardAuthorizationResult.Forbidden;
        foreach (var filter in _filters)
        {
            var result = await filter(context);
            if (result == DashboardAuthorizationResult.Authorized)
                return DashboardAuthorizationResult.Authorized;

            if (result == DashboardAuthorizationResult.Unauthorized)
                denial = DashboardAuthorizationResult.Unauthorized;
        }

        return denial;
    }

    private static async ValueTask<DashboardAuthorizationResult> AuthorizePolicyAsync(
        HttpContext context,
        string? policyName,
        AuthorizationPolicy? policy
    )
    {
        var authorizationService =
            context.RequestServices.GetService<IAuthorizationService>()
            ?? throw new InvalidOperationException(
                "Dashboard ASP.NET Core authorization requires IAuthorizationService. "
                    + "Call services.AddAuthorization() before configuring dashboard authorization policies."
            );

        var result = policyName is not null
            ? await authorizationService.AuthorizeAsync(context.User, context, policyName)
            : await authorizationService.AuthorizeAsync(context.User, context, policy!);

        if (result.Succeeded)
            return DashboardAuthorizationResult.Authorized;

        return IsAuthenticated(context)
            ? DashboardAuthorizationResult.Forbidden
            : DashboardAuthorizationResult.Unauthorized;
    }

    private static bool IsAuthenticated(HttpContext context) =>
        context.User.Identities.Any(identity => identity.IsAuthenticated);
}
