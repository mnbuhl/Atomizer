using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Configuration;

/// <summary>
/// Configures request headers that the embedded dashboard frontend sends to dashboard API endpoints.
/// </summary>
public sealed class DashboardClientRequestHeaderCollection
{
    private const string InvalidHeaderNameChars = "()<>@,;:\\\"/[]?={} \t";
    private readonly List<DashboardClientRequestHeader> _headers = [];

    /// <summary>
    /// Gets the number of configured request headers.
    /// </summary>
    public int Count => _headers.Count;

    /// <summary>
    /// Adds a static request header value.
    /// </summary>
    /// <param name="name">The HTTP header name.</param>
    /// <param name="value">The HTTP header value.</param>
    public void Add(string name, string value)
    {
        ValidateHeaderName(name);
        ValidateHeaderValue(value);

        _headers.Add(new DashboardClientRequestHeader(name, _ => ValueTask.FromResult<string?>(value)));
    }

    /// <summary>
    /// Adds a request header value produced from the current dashboard HTML request.
    /// </summary>
    /// <param name="name">The HTTP header name.</param>
    /// <param name="valueFactory">Creates the HTTP header value. Return <see langword="null"/> to omit the header.</param>
    public void Add(string name, Func<HttpContext, string?> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        ValidateHeaderName(name);

        _headers.Add(new DashboardClientRequestHeader(name, context => ValueTask.FromResult(valueFactory(context))));
    }

    /// <summary>
    /// Adds an asynchronous request header value produced from the current dashboard HTML request.
    /// </summary>
    /// <param name="name">The HTTP header name.</param>
    /// <param name="valueFactory">Creates the HTTP header value. Return <see langword="null"/> to omit the header.</param>
    public void Add(string name, Func<HttpContext, ValueTask<string?>> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        ValidateHeaderName(name);

        _headers.Add(new DashboardClientRequestHeader(name, valueFactory));
    }

    internal async ValueTask<IReadOnlyDictionary<string, string>> BuildAsync(HttpContext context)
    {
        if (_headers.Count == 0)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in _headers)
        {
            var value = await header.GetValueAsync(context);
            if (value is null)
                continue;

            ValidateHeaderValue(value);
            headers[header.Name] = value;
        }

        return headers;
    }

    private static void ValidateHeaderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A header name is required.", nameof(name));

        foreach (var character in name)
        {
            if (character <= 32 || character >= 127 || InvalidHeaderNameChars.IndexOf(character) >= 0)
                throw new ArgumentException($"'{name}' is not a valid HTTP header name.", nameof(name));
        }
    }

    private static void ValidateHeaderValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException(
                "Header values cannot contain carriage return or line feed characters.",
                nameof(value)
            );
    }

    private sealed class DashboardClientRequestHeader
    {
        private readonly Func<HttpContext, ValueTask<string?>> _valueFactory;

        public DashboardClientRequestHeader(string name, Func<HttpContext, ValueTask<string?>> valueFactory)
        {
            Name = name;
            _valueFactory = valueFactory;
        }

        public string Name { get; }

        public ValueTask<string?> GetValueAsync(HttpContext context) => _valueFactory(context);
    }
}
