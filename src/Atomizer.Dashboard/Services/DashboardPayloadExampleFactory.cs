using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atomizer.Dashboard.Services;

internal static class DashboardPayloadExampleFactory
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Create(Type payloadType)
    {
        var example = CreateValue(payloadType, new HashSet<Type>(), depth: 0);
        return JsonSerializer.Serialize(example, Options);
    }

    private static object? CreateValue(Type type, HashSet<Type> stack, int depth)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType is not null)
        {
            return CreateValue(underlyingType, stack, depth);
        }

        if (type == typeof(string) || type == typeof(char))
            return string.Empty;
        if (type == typeof(bool))
            return false;
        if (IsNumeric(type))
            return Activator.CreateInstance(type);
        if (type == typeof(Guid))
            return Guid.Empty;
        if (type == typeof(DateTimeOffset))
            return DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
        if (type == typeof(DateTime))
            return DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime();
        if (type == typeof(TimeSpan))
            return TimeSpan.Zero;
        if (type.IsEnum)
            return Enum.GetValues(type).Length == 0 ? 0 : Enum.GetValues(type).GetValue(0);

        if (depth > 4 || !stack.Add(type))
        {
            return null;
        }

        try
        {
            if (TryCreateDictionary(type, stack, depth, out var dictionary))
            {
                return dictionary;
            }

            if (TryCreateEnumerable(type, stack, depth, out var enumerable))
            {
                return enumerable;
            }

            return CreateObject(type, stack, depth);
        }
        finally
        {
            stack.Remove(type);
        }
    }

    private static Dictionary<string, object?> CreateObject(Type type, HashSet<Type> stack, int depth)
    {
        return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property =>
                property.GetIndexParameters().Length == 0
                && property.GetMethod is not null
                && property.GetCustomAttribute<JsonIgnoreAttribute>() is null
            )
            .OrderBy(property => property.MetadataToken)
            .ToDictionary(GetJsonPropertyName, property => CreateValue(property.PropertyType, stack, depth + 1));
    }

    private static bool TryCreateEnumerable(Type type, HashSet<Type> stack, int depth, out object? enumerable)
    {
        enumerable = null;
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
        {
            return false;
        }

        var elementType = GetEnumerableElementType(type);
        enumerable = elementType is null ? Array.Empty<object>() : new[] { CreateValue(elementType, stack, depth + 1) };
        return true;
    }

    private static bool TryCreateDictionary(Type type, HashSet<Type> stack, int depth, out object? dictionary)
    {
        dictionary = null;
        var dictionaryType = type.GetInterfaces()
            .Concat([type])
            .FirstOrDefault(candidate =>
                candidate.IsGenericType
                && candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>)
                && candidate.GetGenericArguments()[0] == typeof(string)
            );

        if (dictionaryType is null)
        {
            return false;
        }

        var valueType = dictionaryType.GetGenericArguments()[1];
        dictionary = new Dictionary<string, object?> { ["key"] = CreateValue(valueType, stack, depth + 1) };
        return true;
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        return type.GetInterfaces()
            .Concat([type])
            .FirstOrDefault(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            )
            ?.GetGenericArguments()[0];
    }

    private static string GetJsonPropertyName(PropertyInfo property)
    {
        var explicitName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
        return explicitName ?? Options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
    }

    private static bool IsNumeric(Type type)
    {
        return type == typeof(byte)
            || type == typeof(sbyte)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(decimal);
    }
}
