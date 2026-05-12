using System.Text.Json;

namespace Atomizer.Dashboard.Contracts;

internal static class DashboardJsonOptions
{
    internal static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
