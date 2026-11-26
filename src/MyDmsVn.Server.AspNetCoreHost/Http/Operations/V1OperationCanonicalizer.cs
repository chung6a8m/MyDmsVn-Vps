using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using MyDmsVn.Server.Application.Operations;

namespace MyDmsVn.Server.HttpHost;

// Versioned web4-v1 intent, independent of raw JSON order, MVC DTO serialization
// or serializer escaping rules. Only strictly validated source may enter here.
internal static class V1OperationCanonicalizer
{
    internal static (string Name, string Method, string Route) Describe(WebOperationKind kind) => kind switch
    {
        WebOperationKind.SalesCreate => ("Sales.Create", "POST", "/api/v1/sales-receipts"),
        WebOperationKind.SalesUpdate => ("Sales.Update", "PUT", "/api/v1/sales-receipts/{saleId}/draft"),
        WebOperationKind.SalesPost => ("Sales.Post", "POST", "/api/v1/sales-receipts/{saleId}/post"),
        WebOperationKind.ReceiptCreate => ("Receipt.Create", "POST", "/api/v1/goods-receipts"),
        WebOperationKind.ReceiptUpdate => ("Receipt.Update", "PUT", "/api/v1/goods-receipts/{receiptId}/draft"),
        WebOperationKind.ReceiptPost => ("Receipt.Post", "POST", "/api/v1/goods-receipts/{receiptId}/post"),
        _ => throw new V1BindingException()
    };

    internal static WebOperationKind ParseKind(string value) => value switch
    {
        "Sales.Create" => WebOperationKind.SalesCreate,
        "Sales.Update" => WebOperationKind.SalesUpdate,
        "Sales.Post" => WebOperationKind.SalesPost,
        "Receipt.Create" => WebOperationKind.ReceiptCreate,
        "Receipt.Update" => WebOperationKind.ReceiptUpdate,
        "Receipt.Post" => WebOperationKind.ReceiptPost,
        _ => throw new V1BindingException()
    };

    internal static string Canonicalize(WebOperationKind kind, long? targetId, JsonElement validatedSource)
    {
        var description = Describe(kind);
        if ((kind is WebOperationKind.SalesCreate or WebOperationKind.ReceiptCreate ? targetId.HasValue : !targetId.HasValue || targetId <= 0) ||
            validatedSource.ValueKind != JsonValueKind.Object) throw new V1BindingException();

        var result = new StringBuilder("{\"body\":");
        WriteObject(validatedSource, result, body: true, line: false, includeNote: kind is not (WebOperationKind.SalesPost or WebOperationKind.ReceiptPost));
        result.Append(",\"contract\":\"web4-v1\",\"method\":");
        String(description.Method, result);
        result.Append(",\"operationKind\":");
        String(description.Name, result);
        result.Append(",\"requestTargetId\":");
        if (targetId.HasValue) String(targetId.Value.ToString(CultureInfo.InvariantCulture), result);
        else result.Append("null");
        result.Append(",\"routeScope\":");
        String(description.Route, result);
        return result.Append('}').ToString();
    }

    private static void WriteObject(JsonElement value, StringBuilder output, bool body = false, bool line = false, bool includeNote = false)
    {
        var keys = value.EnumerateObject().Select(p => p.Name);
        if (includeNote && !value.TryGetProperty("note", out _)) keys = keys.Append("note");
        output.Append('{');
        var first = true;
        foreach (var name in keys.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!first) output.Append(',');
            first = false;
            String(name, output);
            output.Append(':');
            if (!value.TryGetProperty(name, out var property)) output.Append("null");
            else if (line && name is "qty" or "unitPrice" or "quantity" or "unitCost")
                String(decimal.Parse(property.GetString()!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
                    .ToString("F4", CultureInfo.InvariantCulture), output);
            else if (body && name == "expectedVersion")
                String(Convert.ToBase64String(Convert.FromBase64String(property.GetString()!)), output);
            else if (body && name == "lines")
            {
                output.Append('[');
                var firstLine = true;
                foreach (var item in property.EnumerateArray())
                {
                    if (!firstLine) output.Append(',');
                    firstLine = false;
                    WriteObject(item, output, line: true);
                }
                output.Append(']');
            }
            else WriteValue(property, output);
        }
        output.Append('}');
    }

    private static void WriteValue(JsonElement value, StringBuilder output)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String: String(value.GetString()!, output); break;
            case JsonValueKind.Null: output.Append("null"); break;
            case JsonValueKind.True: output.Append("true"); break;
            case JsonValueKind.False: output.Append("false"); break;
            case JsonValueKind.Number: output.Append(value.GetInt32().ToString(CultureInfo.InvariantCulture)); break;
            default: throw new V1BindingException();
        }
    }

    private static void String(string value, StringBuilder output)
    {
        // U+2028 and U+2029 are intentionally literal, as in the frozen web4-v1
        // byte contract. Do not substitute the default STJ or Json.NET encoder.
        output.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\b': output.Append("\\b"); break;
                case '\t': output.Append("\\t"); break;
                case '\n': output.Append("\\n"); break;
                case '\f': output.Append("\\f"); break;
                case '\r': output.Append("\\r"); break;
                default:
                    if (character < 32) output.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else output.Append(character);
                    break;
            }
        }
        output.Append('"');
    }
}
