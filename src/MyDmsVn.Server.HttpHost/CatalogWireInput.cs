using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
#if VPS05_STJ_INGRESS
using JObject = MyDmsVn.Server.HttpHost.WireObject;
using JArray = MyDmsVn.Server.HttpHost.WireArray;
using JToken = MyDmsVn.Server.HttpHost.WireToken;
using JValue = MyDmsVn.Server.HttpHost.WireValue;
using JProperty = MyDmsVn.Server.HttpHost.WireProperty;
using JTokenType = MyDmsVn.Server.HttpHost.WireTokenType;
#else
using Newtonsoft.Json.Linq;
#endif

namespace MyDmsVn.Server.HttpHost;

// VPS-02: strict, bounded DTO decoding. Never let Json.NET coerce IDs, booleans,
// unknown fields, duplicate keys or query parameter aliases into application requests.
internal static class CatalogWireInput
{
    public static CatalogListRequest List(HttpRequestMessage request)
    {
        var query = SalesWireInput.Query(request, new[] { "search", "pageNumber", "pageSize", "includeInactive" });
        var search = query.TryGetValue("search", out var value) ? value : null;
        if (search != null && (search.Length > 256 || search.IndexOf('\0') >= 0))
            throw new WireInputException("search");
        return new CatalogListRequest(
            search,
            query.TryGetValue("pageNumber", out value) ? Positive(value, "pageNumber") : 1,
            query.TryGetValue("pageSize", out value) ? Positive(value, "pageSize", 200) : 50,
            query.TryGetValue("includeInactive", out value) ? Boolean(value, "includeInactive") : false);
    }

    public static int Id(string text) => Positive(text, "id");
    public static void NoQuery(HttpRequestMessage request) => SalesWireInput.NoQuery(request);
    public static Task EmptyBody(HttpRequestMessage request) => SalesWireInput.EmptyBody(request);

    public static async Task<JObject> Body(HttpRequestMessage request)
    {
        NoQuery(request);
        return (await StrictJsonRequest.Read(request, value => value).ConfigureAwait(false)).Value;
    }

    public static SaveProductRequest Product(JObject source)
    {
        Fields(source, new[] { "code", "name", "unit" }, new[] { "code", "name", "unit" });
        return new SaveProductRequest(RequiredText(source, "code"), RequiredText(source, "name"), RequiredText(source, "unit"));
    }
    public static SaveWarehouseRequest Warehouse(JObject source)
    {
        Fields(source, new[] { "code", "name", "address" }, new[] { "code", "name" });
        return new SaveWarehouseRequest(RequiredText(source, "code"), RequiredText(source, "name"), OptionalText(source, "address"));
    }
    public static SaveEmployeeRequest Employee(JObject source)
    {
        Fields(source, new[] { "code", "name", "phone", "userId" }, new[] { "code", "name" });
        int? userId = null;
        if (source.TryGetValue("userId", StringComparison.Ordinal, out var raw) && raw.Type != JTokenType.Null)
        {
            if (raw.Type != JTokenType.Integer) throw new WireInputException("userId");
            userId = Positive(raw.ToString(), "userId");
        }
        return new SaveEmployeeRequest(RequiredText(source, "code"), RequiredText(source, "name"), OptionalText(source, "phone"), userId);
    }
    public static SaveCustomerRequest Customer(JObject source)
    {
        Fields(source, new[] { "code", "name", "address", "phone", "taxCode" }, new[] { "code", "name" });
        return new SaveCustomerRequest(RequiredText(source, "code"), RequiredText(source, "name"), OptionalText(source, "address"),
            OptionalText(source, "phone"), OptionalText(source, "taxCode"));
    }

    public static bool Active(JObject source)
    {
        Fields(source, new[] { "isActive" }, new[] { "isActive" });
        return source["isActive"]?.Type == JTokenType.Boolean
            ? (bool)source["isActive"]!
            : throw new WireInputException("isActive");
    }

    private static string RequiredText(JObject source, string field) =>
        source[field]?.Type == JTokenType.String ? (string)source[field]! : throw new WireInputException(field);
    private static string? OptionalText(JObject source, string field) =>
        source[field] == null || source[field]!.Type == JTokenType.Null ? null :
        source[field]!.Type == JTokenType.String ? (string)source[field]! : throw new WireInputException(field);
    private static void Fields(JObject source, string[] allowed, string[] required)
    {
        if (source.Properties().Any(field => !allowed.Contains(field.Name, StringComparer.Ordinal)))
            throw new WireInputException();
        foreach (var name in required)
            if (!source.ContainsKey(name)) throw new WireInputException(name);
    }
    private static int Positive(string text, string field, int max = int.MaxValue)
    {
        if (text.Length == 0 || text.Length > 10 || text[0] == '0' ||
            text.Any(c => c < '0' || c > '9') ||
            !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < 1 || value > max)
            throw new WireInputException(field);
        return value;
    }
    private static bool Boolean(string text, string field) => text switch
    {
        "true" => true,
        "false" => false,
        _ => throw new WireInputException(field)
    };
}
