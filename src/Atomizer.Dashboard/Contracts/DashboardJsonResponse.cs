using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Contracts;

internal static class DashboardJsonResponse
{
    internal static async Task WriteAsync<T>(HttpContext context, T value, CancellationToken cancellationToken)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(value, DashboardJsonOptions.CamelCase);
        await context.Response.WriteAsync(json, cancellationToken);
    }
}
