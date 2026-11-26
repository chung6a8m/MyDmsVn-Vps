using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyDmsVn.Server.HttpHost;

// Field-specific conversions from the formatter's validated JSON tokens. No
// global number coercion, culture fallback, rounding, or shared Receipt grammar.
internal static class V1ScalarJsonConverters
{
    internal static string Text(JsonElement value, string field) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new V1BindingException(field);

    internal static int PositiveInt(JsonElement value, string field)
    {
        if (value.ValueKind != JsonValueKind.Number) throw new V1BindingException(field);
        return V1QueryPolicy.PositiveInt(value.GetRawText(), field, int.MaxValue);
    }

    internal static long LineId(JsonElement value, string field)
    {
        var text = Text(value, field);
        return text == "0" ? 0 : V1ScalarBinders.PositiveLong(text, field);
    }

    internal static bool Boolean(JsonElement value, string field) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new V1BindingException(field)
    };

    internal static decimal Decimal(JsonElement value, bool positive, decimal maximum, string field)
    {
        var text = Text(value, field);
        if (text.Length > 64 || !Regex.IsMatch(text, @"\A[0-9]+(?:\.[0-9]{1,4})?\z", RegexOptions.CultureInvariant) ||
            !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ||
            number > maximum || (positive ? number <= 0 : number < 0))
            throw new V1BindingException(field);
        return number;
    }

    internal static string SalesVersion(JsonElement value)
    {
        var text = Text(value, "expectedVersion");
        try
        {
            if (text.Length > 128 || Convert.FromBase64String(text).Length != 8) throw new FormatException();
            return text; // Application receives the original accepted representation.
        }
        catch (FormatException) { throw new V1BindingException("expectedVersion"); }
    }
}
