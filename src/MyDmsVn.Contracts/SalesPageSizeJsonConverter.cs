#if NET10_0_OR_GREATER
using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyDmsVn.Contracts;

internal sealed class SalesPageSizeJsonConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return 50;
        if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out var value))
            throw new JsonException("Sales pageSize must be a 32-bit integer or null.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
#endif
