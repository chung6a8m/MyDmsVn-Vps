using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>
    /// WEB4-v1 six-command fingerprint parity. This isolated copy is intentionally
    /// independent of Server assemblies; verify it against frozen WEB4 corpus.
    /// </summary>
    public static class HttpDesktopOperationFingerprint
    {
        public static string Compute(string kind, long? target, JObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var create = kind == "Sales.Create" || kind == "Receipt.Create";
            var sales = kind.StartsWith("Sales.", StringComparison.Ordinal);
            var post = kind.EndsWith(".Post", StringComparison.Ordinal);
            if (!(create || kind == "Sales.Update" || kind == "Sales.Post" ||
                    kind == "Receipt.Update" || kind == "Receipt.Post") ||
                create == target.HasValue || target.HasValue && target.Value <= 0)
                throw new ArgumentException("Unknown operation identity.", nameof(kind));

            var normalized = (JObject)source.DeepClone();
            if (!post)
            {
                if (normalized["note"] == null)
                    normalized["note"] = JValue.CreateNull();
                if (!(normalized["lines"] is JArray lines))
                    throw new ArgumentException("Lines are required.", nameof(source));
                foreach (var token in lines)
                {
                    if (!(token is JObject line)) throw new ArgumentException("Invalid line.", nameof(source));
                    foreach (var field in sales ? new[] { "qty", "unitPrice" } : new[] { "quantity", "unitCost" })
                    {
                        var value = (string?)line[field] ??
                            throw new ArgumentException("Missing decimal field.", nameof(source));
                        if (!Regex.IsMatch(value, @"\A[0-9]+(?:\.[0-9]{1,4})?\z", RegexOptions.CultureInvariant))
                            throw new ArgumentException("Invalid fixed-point value.", nameof(source));
                        line[field] = decimal.Parse(value,
                            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture)
                            .ToString("F4", CultureInfo.InvariantCulture);
                    }
                }
            }
            if (sales && normalized["expectedVersion"] != null)
            {
                var version = (string?)normalized["expectedVersion"] ??
                    throw new ArgumentException("Invalid version.", nameof(source));
                var bytes = Convert.FromBase64String(version);
                if (bytes.Length != 8) throw new ArgumentException("Invalid version.", nameof(source));
                normalized["expectedVersion"] = Convert.ToBase64String(bytes);
            }

            var scope = kind switch
            {
                "Sales.Create" => "/api/v1/sales-receipts",
                "Sales.Update" => "/api/v1/sales-receipts/{saleId}/draft",
                "Sales.Post" => "/api/v1/sales-receipts/{saleId}/post",
                "Receipt.Create" => "/api/v1/goods-receipts",
                "Receipt.Update" => "/api/v1/goods-receipts/{receiptId}/draft",
                "Receipt.Post" => "/api/v1/goods-receipts/{receiptId}/post",
                _ => throw new ArgumentException("Unknown operation kind.", nameof(kind)),
            };
            var intent = new JObject
            {
                ["contract"] = "web4-v1",
                ["operationKind"] = kind,
                ["method"] = kind.EndsWith(".Update", StringComparison.Ordinal) ? "PUT" : "POST",
                ["routeScope"] = scope,
                ["requestTargetId"] = target.HasValue ?
                    new JValue(target.Value.ToString(CultureInfo.InvariantCulture)) : JValue.CreateNull(),
                ["body"] = normalized,
            };
            var json = new StringBuilder();
            CanonicalJson(intent, json);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(new UTF8Encoding(false, true).GetBytes(json.ToString()));
                return string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        private static void CanonicalJson(JToken token, StringBuilder output)
        {
            if (token is JObject obj)
            {
                output.Append('{');
                var first = true;
                foreach (var property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first) output.Append(',');
                    first = false;
                    JsonString(property.Name, output);
                    output.Append(':');
                    CanonicalJson(property.Value, output);
                }
                output.Append('}');
            }
            else if (token is JArray array)
            {
                output.Append('[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i != 0) output.Append(',');
                    CanonicalJson(array[i], output);
                }
                output.Append(']');
            }
            else if (token.Type == JTokenType.String)
                JsonString((string?)token ?? throw new ArgumentException("Invalid canonical string.", nameof(token)), output);
            else if (token.Type == JTokenType.Null)
                output.Append("null");
            else if (token.Type == JTokenType.Boolean)
                output.Append((bool)token ? "true" : "false");
            else if (token.Type == JTokenType.Integer)
                output.Append(((int)token).ToString(CultureInfo.InvariantCulture));
            else
                throw new ArgumentException("Non-canonical JSON value.", nameof(token));
        }

        private static void JsonString(string value, StringBuilder output)
        {
            output.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"': output.Append("\\\""); break;
                    case '\\': output.Append("\\\\"); break;
                    case '\b': output.Append("\\b"); break;
                    case '\t': output.Append("\\t"); break;
                    case '\n': output.Append("\\n"); break;
                    case '\f': output.Append("\\f"); break;
                    case '\r': output.Append("\\r"); break;
                    default:
                        if (ch < 32)
                            output.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else output.Append(ch);
                        break;
                }
            }
            output.Append('"');
        }
    }
}
