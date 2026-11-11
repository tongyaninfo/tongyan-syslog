using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Syslog.Api.Common;

/// <summary>执行前按唯一工具目录校验参数，未知字段和重复键不得成为复合写入的旁路。</summary>
public static class McpArgumentValidation
{
    public static bool IsValid(JsonElement value, JsonElement schema)
    {
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(item => item.ToString() == value.ToString())) return false;
        switch (schema.GetProperty("type").GetString())
        {
            case "object":
                if (value.ValueKind != JsonValueKind.Object) return false;
                var properties = schema.GetProperty("properties");
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var field in value.EnumerateObject())
                    if (!names.Add(field.Name) || !properties.TryGetProperty(field.Name, out var child) || !IsValid(field.Value, child)) return false;
                return schema.GetProperty("required").EnumerateArray().All(item => names.Contains(item.GetString()!));
            case "string":
                if (value.ValueKind != JsonValueKind.String) return false;
                var text = value.GetString()!;
                if (schema.TryGetProperty("maxLength", out var maxLength) && text.Length > maxLength.GetInt32()) return false;
                if (schema.TryGetProperty("pattern", out var pattern) && !Regex.IsMatch(text, pattern.GetString()!, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return false;
                if (schema.TryGetProperty("format", out var format) && format.GetString() == "date-time")
                    return Regex.IsMatch(text, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
                        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
                return true;
            case "integer":
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
                    && (!schema.TryGetProperty("minimum", out var min) || number >= min.GetInt64())
                    && (!schema.TryGetProperty("maximum", out var max) || number <= max.GetInt64());
            case "boolean": return value.ValueKind is JsonValueKind.True or JsonValueKind.False;
            case "array":
                if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > schema.GetProperty("maxItems").GetInt32()) return false;
                var values = value.EnumerateArray().ToArray();
                return values.All(item => IsValid(item, schema.GetProperty("items")))
                    && values.Select(item => item.ToString()).Distinct(StringComparer.Ordinal).Count() == values.Length;
            default: return false;
        }
    }
}
