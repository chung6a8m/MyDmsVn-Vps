// Net10-only strict JSON DOM. The net48 linked sources retain their Json.NET
// implementation. All nodes here are backed by System.Text.Json.Nodes.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MyDmsVn.Server.HttpHost;

public enum WireTokenType { Object, Array, String, Integer, Float, Boolean, Null }

public abstract class WireToken
{
    internal abstract JsonNode? Node { get; }
    public virtual WireToken? this[string name]
    {
        get => null;
        set => throw new InvalidOperationException("Expected a JSON object.");
    }

    public WireTokenType Type
    {
        get
        {
            if (Node is null) return WireTokenType.Null;
            if (Node is JsonObject) return WireTokenType.Object;
            if (Node is JsonArray) return WireTokenType.Array;
            return Node.GetValueKind() switch
            {
                JsonValueKind.String => WireTokenType.String,
                JsonValueKind.True or JsonValueKind.False => WireTokenType.Boolean,
                JsonValueKind.Null => WireTokenType.Null,
                JsonValueKind.Number => Regex.IsMatch(Node.ToJsonString(), @"\A-?(?:0|[1-9][0-9]*)\z",
                    RegexOptions.CultureInvariant) ? WireTokenType.Integer : WireTokenType.Float,
                _ => throw new JsonException("Invalid wire token.")
            };
        }
    }

    public WireToken DeepClone() => Wrap(Node?.DeepClone());
    internal static WireToken Wrap(JsonNode? value) => value switch
    {
        JsonObject obj => new WireObject(obj),
        JsonArray array => new WireArray(array),
        _ => new WireValue(value)
    };
    internal static JsonNode? Copy(WireToken? value) => value?.Node?.DeepClone();

    public T Value<T>()
    {
        if (typeof(T) == typeof(string)) return (T)(object)((string)this);
        if (typeof(T) == typeof(bool)) return (T)(object)((bool)this);
        throw new InvalidOperationException("Unsupported JSON scalar conversion.");
    }

    public static explicit operator string(WireToken value)
    {
        if (value is null || value.Type != WireTokenType.String)
            throw new InvalidOperationException("Expected JSON string.");
        return value.Node!.GetValue<string>();
    }

    public static explicit operator bool(WireToken value)
    {
        if (value is null || value.Type != WireTokenType.Boolean)
            throw new InvalidOperationException("Expected JSON boolean.");
        return value.Node!.GetValue<bool>();
    }

    public static explicit operator int(WireToken value)
    {
        if (value is null || value.Type != WireTokenType.Integer)
            throw new InvalidOperationException("Expected JSON integer.");
        return int.Parse(value.Node!.ToJsonString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    public static implicit operator WireToken(string? value) => new WireValue(value);
    public static implicit operator WireToken(bool value) => new WireValue(value);
    public static implicit operator WireToken(int value) => new WireValue(value);
    public static implicit operator WireToken(long value) => new WireValue(value);

    public override string ToString() => Type switch
    {
        WireTokenType.String => (string)this,
        WireTokenType.Boolean => ((bool)this).ToString(),
        WireTokenType.Null => string.Empty,
        _ => Node?.ToJsonString() ?? string.Empty
    };
}

public sealed class WireValue : WireToken
{
    private readonly JsonNode? _value;
    public WireValue(string? value) => _value = value == null ? null : JsonValue.Create(value);
    public WireValue(int value) => _value = JsonValue.Create(value);
    public WireValue(long value) => _value = JsonValue.Create(value);
    public WireValue(bool value) => _value = JsonValue.Create(value);
    internal WireValue(JsonNode? value) => _value = value;
    internal override JsonNode? Node => _value;
    public static WireValue CreateNull() => new WireValue((JsonNode?)null);
}

public sealed class WireProperty
{
    public WireProperty(string name, WireToken value)
    { Name = name ?? throw new ArgumentNullException(nameof(name)); Value = value; }
    public string Name { get; }
    public WireToken Value { get; }
}

public sealed class WireObject : WireToken
{
    private JsonObject _obj;
    public WireObject() => _obj = new JsonObject();
    internal WireObject(JsonObject value) => _obj = value;
    internal override JsonNode Node => _obj;
    public int Count => _obj.Count;
    public override WireToken? this[string name]
    {
        get => _obj.TryGetPropertyValue(name, out var value) ? Wrap(value) : null;
        set => _obj[name] = Copy(value);
    }
    public bool ContainsKey(string name) => _obj.ContainsKey(name);
    public bool TryGetValue(string name, StringComparison comparison, out WireToken value)
    {
        if (comparison != StringComparison.Ordinal)
            throw new InvalidOperationException("Only ordinal wire members are allowed.");
        if (_obj.TryGetPropertyValue(name, out var node))
        {
            value = Wrap(node);
            return true;
        }
        value = null!;
        return false;
    }
    public WireProperty? Property(string name, StringComparison comparison) =>
        TryGetValue(name, comparison, out var value) ? new WireProperty(name, value) : null;
    public IEnumerable<WireProperty> Properties() =>
        _obj.Select(item => new WireProperty(item.Key, Wrap(item.Value)));
    public void AddFirst(WireProperty property)
    {
        if (_obj.ContainsKey(property.Name)) throw new JsonException("Duplicate property.");
        var replacement = new JsonObject { [property.Name] = Copy(property.Value) };
        foreach (var entry in _obj) replacement[entry.Key] = entry.Value?.DeepClone();
        _obj = replacement;
    }
    public static WireObject Parse(byte[] bytes, int depth = 16)
    {
        var settings = new JsonDocumentOptions
        {
            MaxDepth = depth,
            CommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false
        };
        using var source = JsonDocument.Parse(bytes, settings);
        if (source.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("The document root must be an object.");
        AssertUniqueProperties(source.RootElement);
        var node = JsonNode.Parse(bytes.AsSpan(), documentOptions: settings) as JsonObject
            ?? throw new JsonException("The document root must be an object.");
        return new WireObject(node);
    }
    private static void AssertUniqueProperties(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in node.EnumerateObject())
                {
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON property.");
                    AssertUniqueProperties(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var value in node.EnumerateArray()) AssertUniqueProperties(value);
                break;
        }
    }
}

public sealed class WireArray : WireToken, IEnumerable<WireToken>
{
    private readonly JsonArray _array;
    public WireArray() => _array = new JsonArray();
    public WireArray(IEnumerable<WireToken> values)
    {
        _array = new JsonArray();
        foreach (var value in values) Add(value);
    }
    internal WireArray(JsonArray array) => _array = array;
    internal override JsonNode Node => _array;
    public int Count => _array.Count;
    public WireToken this[int index] => Wrap(_array[index]);
    public void Add(WireToken? value) => _array.Add(Copy(value));
    public IEnumerator<WireToken> GetEnumerator() => _array.Select(Wrap).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
