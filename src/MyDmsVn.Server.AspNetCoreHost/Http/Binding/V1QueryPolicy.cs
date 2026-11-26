using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace MyDmsVn.Server.HttpHost;

// Preserve the raw v1 query grammar. Request.Query folds case and duplicates;
// neither it nor the framework's permissive numeric parser is a wire oracle.
internal static class V1QueryPolicy
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static async Task RequireEmptyBodyAsync(HttpRequest request)
    {
        // CoreApiAdmissionMiddleware has already buffered and bounded the body.
        // Probe actual bytes, including chunked requests, not just Content-Length.
        var probe = new byte[1];
        if (await request.Body.ReadAsync(probe.AsMemory(), request.HttpContext.RequestAborted)
            .ConfigureAwait(false) != 0)
            throw new V1BindingException();
    }

    internal static Dictionary<string, string> Read(HttpRequest request, params string[] allowed)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var raw = request.QueryString.Value;
        var query = string.IsNullOrEmpty(raw) ? "" : raw.Substring(1);
        if (query.Length == 0) return result;
        foreach (var pair in query.Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator < 1) throw new V1BindingException();
            var name = Decode(pair.Substring(0, separator));
            if (!allowed.Contains(name, StringComparer.Ordinal) || result.ContainsKey(name))
                throw new V1BindingException();
            result.Add(name, Decode(pair.Substring(separator + 1)));
        }
        return result;
    }

    internal static string? OptionalText(Dictionary<string, string> query, string field, int maximum)
    {
        if (!query.TryGetValue(field, out var value)) return null;
        if (value.Length > maximum || value.IndexOf('\0') >= 0) throw new V1BindingException(field);
        try { Utf8.GetByteCount(value); }
        catch (EncoderFallbackException) { throw new V1BindingException(field); }
        return value;
    }

    internal static int PositiveInt(string text, string field, int maximum)
    {
        if (text.Length == 0 || text.Length > 19 || text.Length > 1 && text[0] == '0' ||
            text.Any(c => c < '0' || c > '9') ||
            !int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < 1 || value > maximum)
            throw new V1BindingException(field);
        return value;
    }

    private static string Decode(string value)
    {
        try
        {
            var source = Utf8.GetBytes(value.Replace('+', ' '));
            var bytes = new List<byte>(source.Length);
            for (var index = 0; index < source.Length; index++)
            {
                if (source[index] != '%') { bytes.Add(source[index]); continue; }
                if (index + 2 >= source.Length ||
                    !byte.TryParse(Encoding.ASCII.GetString(source, index + 1, 2),
                        NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var decoded))
                    throw new FormatException();
                bytes.Add(decoded);
                index += 2;
            }
            return Utf8.GetString(bytes.ToArray());
        }
        catch (Exception error) when (error is FormatException or DecoderFallbackException or EncoderFallbackException)
        {
            throw new V1BindingException();
        }
    }
}

internal sealed class V1BindingException : Exception
{
    internal V1BindingException(string? field = null) => Field = field;
    internal string? Field { get; }
}
