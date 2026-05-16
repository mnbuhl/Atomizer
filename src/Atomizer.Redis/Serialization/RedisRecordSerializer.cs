using System.Text.Json;

namespace Atomizer.Redis.Serialization;

internal static class RedisRecordSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Serialize<T>(T record) => JsonSerializer.Serialize(record, JsonOptions);

    public static T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, JsonOptions);
}
