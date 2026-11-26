using System;
using System.Globalization;
using Newtonsoft.Json;

namespace MyDmsVn.Desktop.Infrastructure.Http.Wire
{
    /// <summary>Only explicitly annotated v1 string fields use this writer.
    /// Security revisions and ordinary JSON integer fields remain numeric.</summary>
    public sealed class V1InvariantTextConverter : JsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type objectType) =>
            objectType == typeof(long) || objectType == typeof(decimal);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is long number)
                writer.WriteValue(number.ToString(CultureInfo.InvariantCulture));
            else if (value is decimal amount)
                writer.WriteValue(amount.ToString(CultureInfo.InvariantCulture));
            else
                throw new JsonSerializationException("Expected a typed v1 decimal or Int64.");
        }

        public override object ReadJson(JsonReader reader, Type objectType,
            object? existingValue, JsonSerializer serializer) => throw new NotSupportedException();
    }
}
