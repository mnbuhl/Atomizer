using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atomizer.Dashboard.Contracts;

internal static class DashboardJsonOptions
{
    internal static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
