using System;
using System.Collections.Generic;
using Microsoft.OpenApi;
using MyDmsVn.Server.HttpHost.Features.Receipts;

namespace MyDmsVn.Server.HttpHost.Features.Inventory;

internal static class V1Web3OpenApi
{
    internal static void Describe(OpenApiSchema schema, Type type, string? propertyName, string? parameterName)
    {
        var isPage = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Web3PageResponse<>);
        var isData = isPage || type == typeof(ReceiptDetailResponse) || type == typeof(ReceiptPostResponse);
        var isEnvelope = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(V1SuccessEnvelope<>) &&
            (type.GetGenericArguments()[0] == typeof(ReceiptDetailResponse) ||
             type.GetGenericArguments()[0] == typeof(ReceiptPostResponse) ||
             type.GetGenericArguments()[0] == typeof(Web3PageResponse<ReceiptDetailResponse>) ||
             type.GetGenericArguments()[0] == typeof(Web3PageResponse<StockBalanceResponse>) ||
             type.GetGenericArguments()[0] == typeof(Web3PageResponse<StockCardResponse>));
        if (isEnvelope)
        {
            schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
            schema.Required.Add("data");
        }
        if (isData && propertyName == "data")
        {
            schema.Type = JsonSchemaType.Object;
            schema.Metadata?.Remove("x-is-nullable-property");
        }
        if (parameterName is "fromUtc" or "toUtc")
        {
            schema.Format = "date-time";
            schema.Pattern = @"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,7})?Z$";
        }

        if (schema.Properties != null && (type == typeof(ReceiptCreateRequest) || type == typeof(ReceiptUpdateRequest) ||
            type == typeof(ReceiptPostRequest) || type == typeof(ReceiptSaveLineRequest)))
        {
            schema.Required = new HashSet<string>(System.Linq.Enumerable.Where(schema.Properties.Keys, name => name != "note"), StringComparer.Ordinal);
            foreach (var name in new[] { "quantity", "unitCost" })
                if (schema.Properties.TryGetValue(name, out var property) && property is OpenApiSchema number)
                {
                    number.Type = JsonSchemaType.String;
                    number.Format = null;
                    number.Pattern = @"^[0-9]+(\.[0-9]{1,4})?$";
                    number.MaxLength = 64;
                    number.Description = name == "quantity" ? "Strictly positive invariant decimal, scale 0–4." : "Nonnegative invariant decimal, scale 0–4.";
                }
            if (schema.Properties.TryGetValue("expectedVersion", out var version) && version is OpenApiSchema token)
            {
                token.MinLength = 12;
                token.MaxLength = 12;
                token.Pattern = "^[A-Za-z0-9+/]{10}[AEIMQUYcgkosw048]=$";
                token.Description = "Canonical Base64 encoding of exactly eight bytes.";
            }
            if (schema.Properties.TryGetValue("lines", out var lines) && lines is OpenApiSchema array)
            {
                array.MinItems = 1;
                array.MaxItems = 200;
            }
            if (schema.Properties.TryGetValue("note", out var note) && note is OpenApiSchema text)
                text.MaxLength = 1000;
            return;
        }

        if (schema.Properties == null || !(isPage || type == typeof(ReceiptDetailResponse) ||
            type == typeof(ReceiptLineResponse) || type == typeof(ReceiptPostResponse) || type == typeof(StockBalanceResponse) || type == typeof(StockCardResponse)))
            return;

        // Unlike Sales, all WEB3 response fields are present, even if null.
        schema.Required = new HashSet<string>(schema.Properties.Keys, StringComparer.Ordinal);
        foreach (var (name, property) in schema.Properties)
        {
            if (property is not OpenApiSchema scalar) continue;
            if (name is "warehouseDisplayName" or "employeeDisplayName" or "productDisplayName" or "note" or "averageUnitCost" ||
                name == "postedAtUtc" && type == typeof(ReceiptDetailResponse))
                scalar.Type = JsonSchemaType.String | JsonSchemaType.Null;
            if (name == "postedAtUtc") scalar.Format = "date-time";
            if (name is "receiptId" or "receiptLineId" or "ledgerId" or "documentId" or "totalCount")
            {
                scalar.Type = JsonSchemaType.String;
                scalar.Pattern = name == "totalCount" ? "^(0|[1-9][0-9]{0,18})$" : "^[1-9][0-9]{0,18}$";
            }
            if (name is "quantity" or "unitCost" or "inventoryValue")
                scalar.Pattern = @"^[0-9]+\.[0-9]{4}$";
            if (name is "quantityChange" or "valueChange")
                scalar.Pattern = @"^-?[0-9]+\.[0-9]{4}$";
            if (name == "averageUnitCost")
                scalar.Pattern = @"^[0-9]+\.[0-9]{8}$";
        }
    }
}
