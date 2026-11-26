using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.HttpHost.Features.Catalog;

// The transport allowlist is distinct from Application business validation.
// Required text is not trimmed here; null/missing/incorrect tokens are rejected,
// while the Application owns field length, normalization, uniqueness and references.
public sealed record ProductSaveBody([property: JsonRequired] string Code, [property: JsonRequired] string Name, [property: JsonRequired] string Unit)
{
    internal SaveProductRequest ToApplication() => new(Code, Name, Unit);
}

public sealed record WarehouseSaveBody([property: JsonRequired] string Code, [property: JsonRequired] string Name, string? Address)
{
    internal SaveWarehouseRequest ToApplication() => new(Code, Name, Address);
}

public sealed record EmployeeSaveBody([property: JsonRequired] string Code, [property: JsonRequired] string Name, string? Phone, int? UserId)
{
    internal SaveEmployeeRequest ToApplication() => new(Code, Name, Phone, UserId);
}

public sealed record CustomerSaveBody([property: JsonRequired] string Code, [property: JsonRequired] string Name, string? Address, string? Phone, string? TaxCode)
{
    internal SaveCustomerRequest ToApplication() => new(Code, Name, Address, Phone, TaxCode);
}

public sealed record CatalogActiveBody([property: JsonRequired] bool IsActive);

internal static class CatalogCrudRequestReader
{
    internal static bool Supports(Type type) =>
        type == typeof(ProductSaveBody) ||
        type == typeof(WarehouseSaveBody) ||
        type == typeof(EmployeeSaveBody) ||
        type == typeof(CustomerSaveBody) ||
        type == typeof(CatalogActiveBody);

    internal static object Read(Type type, JsonElement body)
    {
        if (type == typeof(ProductSaveBody))
        {
            Fields(body, new[] { "code", "name", "unit" }, new[] { "code", "name", "unit" });
            return new ProductSaveBody(V1ScalarJsonConverters.Text(body.GetProperty("code"), "code"),
                V1ScalarJsonConverters.Text(body.GetProperty("name"), "name"),
                V1ScalarJsonConverters.Text(body.GetProperty("unit"), "unit"));
        }
        if (type == typeof(WarehouseSaveBody))
        {
            Fields(body, new[] { "code", "name", "address" }, new[] { "code", "name" });
            return new WarehouseSaveBody(V1ScalarJsonConverters.Text(body.GetProperty("code"), "code"),
                V1ScalarJsonConverters.Text(body.GetProperty("name"), "name"),
                OptionalText(body, "address"));
        }
        if (type == typeof(EmployeeSaveBody))
        {
            Fields(body, new[] { "code", "name", "phone", "userId" }, new[] { "code", "name" });
            int? userId = body.TryGetProperty("userId", out var raw) && raw.ValueKind != JsonValueKind.Null
                ? V1ScalarJsonConverters.PositiveInt(raw, "userId") : null;
            return new EmployeeSaveBody(V1ScalarJsonConverters.Text(body.GetProperty("code"), "code"),
                V1ScalarJsonConverters.Text(body.GetProperty("name"), "name"),
                OptionalText(body, "phone"),
                userId);
        }
        if (type == typeof(CustomerSaveBody))
        {
            Fields(body, new[] { "code", "name", "address", "phone", "taxCode" }, new[] { "code", "name" });
            return new CustomerSaveBody(V1ScalarJsonConverters.Text(body.GetProperty("code"), "code"),
                V1ScalarJsonConverters.Text(body.GetProperty("name"), "name"),
                OptionalText(body, "address"),
                OptionalText(body, "phone"),
                OptionalText(body, "taxCode"));
        }
        if (type == typeof(CatalogActiveBody))
        {
            Fields(body, new[] { "isActive" }, new[] { "isActive" });
            return new CatalogActiveBody(V1ScalarJsonConverters.Boolean(body.GetProperty("isActive"), "isActive"));
        }
        throw new InvalidOperationException("Unsupported Catalog input.");
    }

    private static void Fields(JsonElement body, string[] allowed, string[] required)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            body.EnumerateObject().Any(p => !allowed.Contains(p.Name, StringComparer.Ordinal)))
            throw new V1BindingException();
        foreach (var name in required)
            if (!body.TryGetProperty(name, out _)) throw new V1BindingException(name);
    }

    private static string? OptionalText(JsonElement body, string field) =>
        body.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null
            ? V1ScalarJsonConverters.Text(value, field) : null;
}
