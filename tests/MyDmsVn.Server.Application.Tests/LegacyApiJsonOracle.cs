#if NET10_0_OR_GREATER
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.Application.Tests;

// The old net48 Json.NET transport contract is exercised as a test-only oracle.
// The shipped net10 Contracts assembly deliberately has no Json.NET reference.
internal static class LegacyApiJsonOracle
{
    internal static JsonSerializerSettings CreateSerializerSettings() =>
        CreateSerializerSettings(ApiStatusCode.Unspecified);

    internal static ApiResponse<T> DeserializeResponse<T>(string json, ApiStatusCode responseStatus)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Response JSON is required.", nameof(json));
        if (responseStatus == ApiStatusCode.Unspecified)
            throw new ArgumentOutOfRangeException(nameof(responseStatus));
        return JsonConvert.DeserializeObject<ApiResponse<T>>(json,
            CreateSerializerSettings(responseStatus))!;
    }

    private static JsonSerializerSettings CreateSerializerSettings(ApiStatusCode status)
    {
        var settings = new JsonSerializerSettings
        {
            ContractResolver = new LegacyResolver(),
            Culture = CultureInfo.InvariantCulture,
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        };
        settings.Converters.Add(new LegacyApiResponseConverter(status));
        return settings;
    }

    private sealed class LegacyResolver : CamelCasePropertyNamesContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization serialization)
        {
            var property = base.CreateProperty(member, serialization);
            if ((member.Name == "IsSuccess" && member.DeclaringType?.IsGenericType == true &&
                 member.DeclaringType.GetGenericTypeDefinition() == typeof(ApiResponse<>)) ||
                (member.Name == "Status" && member.DeclaringType == typeof(ApiError)))
                property.Ignored = true;
            if ((property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?)) &&
                member.GetCustomAttribute<System.Text.Json.Serialization.JsonConverterAttribute>() != null)
                property.Converter = new LegacyCalendarDateConverter();
            return property;
        }

        protected override JsonObjectContract CreateObjectContract(Type type)
        {
            var contract = base.CreateObjectContract(type);
            // Json.NET's original [JsonConstructor] markers became STJ markers
            // in production. Respect them *only inside the oracle* on net10.
            var marked = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Where(c => c.IsDefined(typeof(System.Text.Json.Serialization.JsonConstructorAttribute), false))
                .ToArray();
            if (marked.Length > 1) throw new JsonSerializationException("Multiple JSON constructors.");
            if (marked.Length != 1) return contract;
            var constructor = marked[0];
            contract.OverrideCreator = args => constructor.Invoke(args);
            contract.CreatorParameters.Clear();
            foreach (var parameter in constructor.GetParameters())
            {
                var sourceProperty = contract.Properties.GetClosestMatchProperty(parameter.Name!);
                contract.CreatorParameters.Add(new JsonProperty
                {
                    PropertyName = sourceProperty?.PropertyName ?? parameter.Name!,
                    PropertyType = parameter.ParameterType,
                    Readable = true,
                    Writable = true,
                    Converter = sourceProperty?.Converter
                });
            }
            return contract;
        }
    }

    private sealed class LegacyCalendarDateConverter : JsonConverter
    {
        public override bool CanConvert(Type type) =>
            type == typeof(DateTime) || type == typeof(DateTime?);
        public override object? ReadJson(JsonReader reader, Type type, object? existing,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null && type == typeof(DateTime?)) return null;
            if (reader.TokenType != JsonToken.String || reader.Value is not string text ||
                !DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                throw new JsonSerializationException("A calendar date must use yyyy-MM-dd.");
            return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        }
        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is DateTime date)
                writer.WriteValue(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            else writer.WriteNull();
        }
    }

    private sealed class LegacyApiResponseConverter : JsonConverter
    {
        private readonly ApiStatusCode _status;
        internal LegacyApiResponseConverter(ApiStatusCode status) => _status = status;
        public override bool CanWrite => false;
        public override bool CanConvert(Type type) =>
            type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ApiResponse<>);

        public override object ReadJson(JsonReader reader, Type type, object? existing,
            JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            var data = obj.GetValue("data", StringComparison.OrdinalIgnoreCase);
            var error = obj.GetValue("error", StringComparison.OrdinalIgnoreCase);
            if ((data == null) == (error == null))
                throw new JsonSerializationException("An API response must contain exactly one of data or error.");
            if (data != null)
            {
                if (data.Type == JTokenType.Null)
                    throw new JsonSerializationException("Response data cannot be null.");
                return Invoke(type, "Success", data.ToObject(type.GetGenericArguments()[0], serializer));
            }
            if (error!.Type == JTokenType.Null)
                throw new JsonSerializationException("Response error cannot be null.");
            var details = error["details"] is not JToken detailsToken ||
                detailsToken.Type == JTokenType.Null
                ? Array.Empty<ApiErrorDetail>()
                : detailsToken.ToObject<ApiErrorDetail[]>(serializer) ?? Array.Empty<ApiErrorDetail>();
            try
            {
                var parsed = new ApiError(_status,
                    error.Value<string>("code")!, error.Value<string>("message")!,
                    details, error.Value<string>("correlationId"));
                return Invoke(type, "Failure", parsed);
            }
            catch (ArgumentException exception)
            {
                throw new JsonSerializationException("The API error payload is invalid.", exception);
            }
        }

        private static object Invoke(Type type, string method, object? value)
        {
            try { return type.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new[] { value })!; }
            catch (TargetInvocationException exception)
            {
                throw new JsonSerializationException("The API response payload is invalid.",
                    exception.InnerException ?? exception);
            }
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
            throw new NotSupportedException();
    }
}
#endif
