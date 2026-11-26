using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace MyDmsVn.Server.HttpHost;

// Strict, bounded parsing of signed JWT/WEB6 token claims. Do not use
// serializer coercion or last-wins duplicate handling before signature checks.
internal static class SignedTokenJson
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static JsonElement ReadObject(string encoded)
    {
        var bytes = Base64UrlEncoder.DecodeBytes(encoded);
        Utf8.GetCharCount(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            MaxDepth = 8,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("The token payload must be an object.");
        RequireUniqueMembers(document.RootElement);
        return document.RootElement.Clone();
    }

    private static void RequireUniqueMembers(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate token member.");
                RequireUniqueMembers(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                RequireUniqueMembers(item);
    }

    internal static JsonElement Member(JsonElement value, string name) =>
        value.TryGetProperty(name, out var member) ? member : default;

    internal static string? Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
