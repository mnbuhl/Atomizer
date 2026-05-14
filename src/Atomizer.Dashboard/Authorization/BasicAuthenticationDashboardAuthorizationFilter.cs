using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Authorization;

/// <summary>
/// Authorizes Atomizer Dashboard requests using HTTP Basic authentication.
/// </summary>
public sealed class BasicAuthenticationDashboardAuthorizationFilter : IAtomizerDashboardAuthorizationFilter
{
    private readonly Func<HttpContext, string, string, ValueTask<bool>> _validateCredentials;
    private readonly DashboardBasicAuthenticationOptions _options;

    /// <summary>
    /// Creates a Basic authentication filter with fixed credentials.
    /// </summary>
    /// <param name="username">The required username.</param>
    /// <param name="password">The required password.</param>
    /// <param name="configure">Optional Basic authentication configuration.</param>
    public BasicAuthenticationDashboardAuthorizationFilter(
        string username,
        string password,
        Action<DashboardBasicAuthenticationOptions>? configure = null
    )
        : this(CreateFixedCredentialValidator(username, password), configure) { }

    /// <summary>
    /// Creates a Basic authentication filter with custom credential validation.
    /// </summary>
    /// <param name="validateCredentials">Validates the current request, username, and password.</param>
    /// <param name="configure">Optional Basic authentication configuration.</param>
    public BasicAuthenticationDashboardAuthorizationFilter(
        Func<HttpContext, string, string, ValueTask<bool>> validateCredentials,
        Action<DashboardBasicAuthenticationOptions>? configure = null
    )
    {
        _validateCredentials = validateCredentials ?? throw new ArgumentNullException(nameof(validateCredentials));
        _options = new DashboardBasicAuthenticationOptions();
        configure?.Invoke(_options);
        _options.Validate();
    }

    /// <inheritdoc />
    public async ValueTask<DashboardAuthorizationResult> AuthorizeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_options.RequireHttps && !context.Request.IsHttps)
            return DashboardAuthorizationResult.Forbidden;

        if (!TryReadCredentials(context, out var username, out var password))
        {
            Challenge(context);
            return DashboardAuthorizationResult.Unauthorized;
        }

        if (await _validateCredentials(context, username, password))
            return DashboardAuthorizationResult.Authorized;

        Challenge(context);
        return DashboardAuthorizationResult.Unauthorized;
    }

    private static Func<HttpContext, string, string, ValueTask<bool>> CreateFixedCredentialValidator(
        string username,
        string password
    )
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("A Basic authentication username is required.", nameof(username));

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("A Basic authentication password is required.", nameof(password));

        return (_, actualUsername, actualPassword) =>
            ValueTask.FromResult(FixedTimeEquals(username, actualUsername) & FixedTimeEquals(password, actualPassword));
    }

    private static bool TryReadCredentials(HttpContext context, out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        if (
            !AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization.ToString(), out var header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter)
        )
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separatorIndex = decoded.IndexOf(':');
        if (separatorIndex < 0)
            return false;

        username = decoded[..separatorIndex];
        password = decoded[(separatorIndex + 1)..];
        return true;
    }

    private void Challenge(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate =
            $"Basic realm=\"{EscapeHeaderValue(_options.Realm)}\", charset=\"UTF-8\"";
    }

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);

        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static string EscapeHeaderValue(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
