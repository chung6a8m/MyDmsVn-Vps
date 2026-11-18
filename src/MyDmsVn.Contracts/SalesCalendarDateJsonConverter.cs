#if NET10_0_OR_GREATER
using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyDmsVn.Contracts;

// A net10-only converter for shared DTO metadata. The Desktop/net48
// Newtonsoft converter is compiled exclusively in the legacy graph.
public sealed class SalesCalendarDateJsonConverter : JsonConverter<DateTime>
{
    public static DateTime Normalize(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String ||
            !DateTime.TryParseExact(reader.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date))
            throw new JsonException("A sales calendar date must use yyyy-MM-dd.");
        return Normalize(date);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}
#else
using System;
using System.Globalization;
using Newtonsoft.Json;
namespace MyDmsVn.Contracts;
public sealed class SalesCalendarDateJsonConverter : JsonConverter
{
    public override bool CanConvert(Type type) => type == typeof(DateTime) || type == typeof(DateTime?);
    public static DateTime Normalize(DateTime value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        if (reader.TokenType == JsonToken.Null && objectType == typeof(DateTime?)) return null;
        if (reader.TokenType != JsonToken.String || reader.Value is not string text ||
            !DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new JsonSerializationException("A sales calendar date must use yyyy-MM-dd.");
        return Normalize(date);
    }
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value == null) { writer.WriteNull(); return; }
        writer.WriteValue(((DateTime)value).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
#endif
