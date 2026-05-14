namespace Atomizer.Dashboard.Authorization;

/// <summary>
/// Configures the built-in Atomizer Dashboard Basic authentication filter.
/// </summary>
public sealed class DashboardBasicAuthenticationOptions
{
    /// <summary>
    /// Gets or sets the Basic authentication realm displayed by clients. Defaults to <c>Atomizer Dashboard</c>.
    /// </summary>
    public string Realm { get; set; } = "Atomizer Dashboard";

    /// <summary>
    /// Gets or sets whether Basic authentication requires HTTPS. Defaults to <see langword="true"/>.
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Realm))
            throw new ArgumentException("A Basic authentication realm is required.", nameof(Realm));

        if (Realm.Contains('\r') || Realm.Contains('\n'))
            throw new ArgumentException("The Basic authentication realm cannot contain line breaks.", nameof(Realm));
    }
}
