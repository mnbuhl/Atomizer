using System.Reflection;
using Atomizer.Dashboard.Configuration;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.StaticFiles;

internal static class EmbeddedSpaFileProvider
{
    private static readonly Assembly Assembly = typeof(EmbeddedSpaFileProvider).Assembly;
    private const string ResourcePrefix = "Atomizer.Dashboard.Spa.";
    private static string? _cachedIndexHtml;

    /// <summary>
    /// Serves the embedded SPA asset matching the request path, or falls back to
    /// <c>index.html</c> for unmatched paths to support client-side routing.
    /// </summary>
    public static async Task ServeAsync(HttpContext context, string routePrefix, DashboardOptions options)
    {
        var rawPath = context.Request.Path.Value ?? string.Empty;
        var prefix = routePrefix.TrimEnd('/');
        var path = rawPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? rawPath[prefix.Length..].TrimStart('/')
            : rawPath.TrimStart('/');
        var resourceName = ResourcePrefix + path;

        using var stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            context.Response.ContentType = GetContentType(path);
            context.Response.Headers.ETag = GetETag();
            await stream.CopyToAsync(context.Response.Body);
            return;
        }

        await ServeIndexAsync(context, routePrefix, options);
    }

    public static async Task ServeIndexAsync(HttpContext context, string routePrefix, DashboardOptions options)
    {
        var html = _cachedIndexHtml ??= BuildIndexHtml(options, routePrefix);

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.ETag = GetETag();
        await context.Response.WriteAsync(html);
    }

    private static string BuildIndexHtml(DashboardOptions options, string routePrefix)
    {
        using var stream = Assembly.GetManifestResourceStream(ResourcePrefix + "index.html");
        if (stream is null)
            throw new InvalidOperationException("Embedded index.html not found in Atomizer.Dashboard assembly.");

        var baseHref = routePrefix.TrimEnd('/') + "/";

        using var reader = new StreamReader(stream);
        return reader
            .ReadToEnd()
            .Replace("<head>", $"<head>\n    <base href=\"{baseHref}\">")
            .Replace("{{ROUTE_PREFIX}}", routePrefix)
            .Replace("{{TITLE}}", options.Title)
            .Replace("{{STATS_REFRESH_MS}}", ((int)options.StatsRefreshInterval.TotalMilliseconds).ToString())
            .Replace("{{JOBS_REFRESH_MS}}", ((int)options.JobsRefreshInterval.TotalMilliseconds).ToString());
    }

    private static string GetETag()
    {
        var name = Assembly.GetName();
        return $"\"{name.Version?.ToString() ?? name.FullName.GetHashCode().ToString("x")}\"";
    }

    private static string GetContentType(string path) =>
        Path.GetExtension(path) switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "application/javascript",
            ".mjs" => "application/javascript",
            ".css" => "text/css",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".png" => "image/png",
            ".woff2" => "font/woff2",
            ".woff" => "font/woff",
            _ => "application/octet-stream",
        };
}
