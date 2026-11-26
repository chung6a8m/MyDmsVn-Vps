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
        var isData = isPage || type == typeof(ReceiptDetailResponse);
        var isEnvelope = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(V1SuccessEnvelope<>) &&
            (type.GetGenericArguments()[0] == typeof(ReceiptDetailResponse) ||
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

        if (schema.Properties == null || !(isPage || type == typeof(ReceiptDetailResponse) ||
            type == typeof(ReceiptLineResponse) || type == typeof(StockBalanceResponse) || type == typeof(StockCardResponse)))
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
