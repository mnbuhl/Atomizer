using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atomizer.Dashboard.Tests;

internal static class TestJsonOptions
{
    internal static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}
