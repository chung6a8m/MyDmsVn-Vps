using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application.Operations;
using MyDmsVn.Server.Domain.Inventory;
using MyDmsVn.Server.HttpHost.Features.Sales;
using MyDmsVn.Server.HttpHost.Features.Receipts;

namespace MyDmsVn.Server.HttpHost;

// Pure native HTTP codec. The historical net48/source-linked codec stays an oracle,
// not a dependency of this reader. Preserve original stored JSON for status disclosure.
public sealed class V1OperationResultCodec : IWebOperationResultCodec
{
    public const int MaximumResultBytes = 1048576;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        V1HttpJson.Configure(options);
        return options;
    }

    public byte[] Encode<T>(WebOperationKind kind, T originalResult)
    {
        try
        {
            object projected = kind switch
            {
                WebOperationKind.SalesCreate or WebOperationKind.SalesUpdate when originalResult is SalesReceiptDto sale && sale.Lines.Count <= 200 => SalesHttpMapper.Detail(sale),
                WebOperationKind.SalesPost when originalResult is PostSalesReceiptResponse salePost => SalesHttpMapper.Post(salePost),
                WebOperationKind.ReceiptCreate or WebOperationKind.ReceiptUpdate when originalResult is GoodsReceiptDto receipt => ReceiptHttpMapper.Detail(receipt),
                WebOperationKind.ReceiptPost when originalResult is PostGoodsReceiptResponse receiptPost => ReceiptHttpMapper.Post(receiptPost),
                _ => throw Unavailable()
            };
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new V1SuccessEnvelope<object>(projected), Json);
            _ = DecodeEnvelope(kind, bytes);
            return bytes;
        }
        catch (Exception error) when (CodecError(error)) { throw Unavailable(); }
    }

    public T Decode<T>(WebOperationKind kind, byte[] bytes)
    {
        try
        {
            var data = DecodeEnvelope(kind, bytes).GetProperty("data");
            object value = kind switch
            {
                WebOperationKind.SalesCreate or WebOperationKind.SalesUpdate => Sales(data),
                WebOperationKind.SalesPost => new PostSalesReceiptResponse(Id(Get(data, "saleId")), "Posted", Utc(Get(data, "postedAtUtc")), Version(Get(data, "version"))),
                WebOperationKind.ReceiptCreate or WebOperationKind.ReceiptUpdate => Receipt(data),
                WebOperationKind.ReceiptPost => new PostGoodsReceiptResponse(Id(Get(data, "receiptId")), "Posted", Utc(Get(data, "postedAtUtc"))),
                _ => throw Unavailable()
            };
            return value is T typed ? typed : throw Unavailable();
        }
        catch (Exception error) when (CodecError(error)) { throw Unavailable(); }
    }

    public JsonElement DecodeEnvelope(WebOperationKind kind, byte[] bytes)
    {
        try
        {
            Require(bytes != null && bytes.Length is > 0 and <= MaximumResultBytes);
            // Throw on malformed UTF-8 before materialization; never repair stored bytes.
            _ = Utf8.GetCharCount(bytes!);
            using var document = JsonDocument.Parse(bytes!, new JsonDocumentOptions { MaxDepth = 16 });
            var envelope = document.RootElement;
            Shape(envelope, "data");
            var data = Get(envelope, "data");
            Require(data.ValueKind == JsonValueKind.Object);
            switch (kind)
            {
                case WebOperationKind.SalesCreate:
                case WebOperationKind.SalesUpdate: Sales(data); break;
                case WebOperationKind.ReceiptCreate:
                case WebOperationKind.ReceiptUpdate: Receipt(data); break;
                case WebOperationKind.SalesPost:
                    Shape(data, "saleId,status,postedAtUtc,version"); Id(Get(data, "saleId")); Equal(Get(data, "status"), "Posted"); Utc(Get(data, "postedAtUtc")); Version(Get(data, "version")); break;
                case WebOperationKind.ReceiptPost:
                    Shape(data, "receiptId,status,postedAtUtc"); Id(Get(data, "receiptId")); Equal(Get(data, "status"), "Posted"); Utc(Get(data, "postedAtUtc")); break;
                default: throw Unavailable();
            }
            return envelope.Clone();
        }
        catch (Exception error) when (CodecError(error)) { throw Unavailable(); }
    }

    private static JsonElement Get(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property) ? property : default;

    private static SalesReceiptDto Sales(JsonElement data)
    {
        Shape(data, "saleId,saleNumber,saleDate,customerId,warehouseId,employeeId,status,version,lines,totalAmount,customerIsActive,warehouseIsActive,employeeIsActive,customerDisplayName,warehouseDisplayName,employeeDisplayName", "note");
        Equal(Get(data, "status"), "Draft"); Active(Get(data, "customerIsActive")); Active(Get(data, "warehouseIsActive")); Active(Get(data, "employeeIsActive"));
        var saleId = Id(Get(data, "saleId")); var number = Number(Get(data, "saleNumber"), "SA"); var date = Date(Get(data, "saleDate"));
        var customer = Int(Get(data, "customerId")); var warehouse = Int(Get(data, "warehouseId")); var employee = Int(Get(data, "employeeId"));
        var customerName = Text(Get(data, "customerDisplayName"), 291); var warehouseName = Text(Get(data, "warehouseDisplayName"), 291); var employeeName = Text(Get(data, "employeeDisplayName"), 291);
        var version = Version(Get(data, "version")); var note = Get(data, "note").ValueKind == JsonValueKind.Undefined ? null : Text(Get(data, "note"), 1000);
        var lines = new List<SalesReceiptLineDto>(); var ids = new HashSet<long>();
        foreach (var token in Lines(Get(data, "lines")).EnumerateArray())
        {
            Require(token.ValueKind == JsonValueKind.Object); var line = token;
            Shape(line, "saleLineId,lineNo,productId,qty,unitPrice,isGift,lineAmount,productIsActive,unit,productDisplayName");
            var id = Id(Get(line, "saleLineId")); Require(ids.Add(id)); var lineNo = Int(Get(line, "lineNo")); Require(lineNo == lines.Count + 1);
            var product = Int(Get(line, "productId")); var unit = Text(Get(line, "unit"), 32); Require(!string.IsNullOrWhiteSpace(unit));
            var productName = Text(Get(line, "productDisplayName"), 291); Active(Get(line, "productIsActive"));
            var quantity = Decimal(Get(line, "qty"), SalesContractLimits.MaximumQuantity, true);
            var price = Decimal(Get(line, "unitPrice"), SalesContractLimits.MaximumUnitPrice);
            Require(Get(line, "isGift").ValueKind is JsonValueKind.True or JsonValueKind.False); var gift = Get(line, "isGift").GetBoolean();
            var amount = Decimal(Get(line, "lineAmount"), SalesContractLimits.MaximumAmount);
            Require(amount == InventoryValuationMath.CalculateRevenue(quantity, price, gift));
            lines.Add(new SalesReceiptLineDto(id, lineNo, product, unit, quantity, price, gift, amount, null, null, productName, true));
        }
        var total = Decimal(Get(data, "totalAmount"), SalesContractLimits.MaximumAmount);
        Require(total == InventoryValuationMath.SumAmounts(lines.Select(line => line.LineAmount)));
        return new SalesReceiptDto(saleId, number, date, customer, warehouse, employee, "Draft", note, null, version, lines,
            total, null, null, customerName, true, warehouseName, true, employeeName, true);
    }

    private static GoodsReceiptDto Receipt(JsonElement data)
    {
        Shape(data, "receiptId,receiptNumber,receiptDate,warehouseId,warehouseDisplayName,warehouseIsActive,employeeId,employeeDisplayName,employeeIsActive,status,note,postedAtUtc,version,lines");
        Equal(Get(data, "status"), "Draft"); Null(Get(data, "postedAtUtc")); Null(Get(data, "warehouseDisplayName")); Null(Get(data, "employeeDisplayName"));
        Active(Get(data, "warehouseIsActive")); Active(Get(data, "employeeIsActive"));
        var receiptId = Id(Get(data, "receiptId")); var number = Number(Get(data, "receiptNumber"), "GR"); var date = Date(Get(data, "receiptDate"));
        var warehouse = Int(Get(data, "warehouseId")); var employee = Int(Get(data, "employeeId")); var version = Version(Get(data, "version"));
        var note = Get(data, "note").ValueKind == JsonValueKind.Null ? null : Text(Get(data, "note"), 1000);
        var lines = new List<GoodsReceiptLineDto>(); var ids = new HashSet<long>(); var products = new HashSet<int>();
        foreach (var token in Lines(Get(data, "lines")).EnumerateArray())
        {
            Require(token.ValueKind == JsonValueKind.Object); var line = token;
            Shape(line, "receiptLineId,lineNumber,productId,quantity,unitCost,productDisplayName,productIsActive");
            var id = Id(Get(line, "receiptLineId")); Require(ids.Add(id)); var lineNo = Int(Get(line, "lineNumber")); Require(lineNo == lines.Count + 1);
            var product = Int(Get(line, "productId")); Require(products.Add(product)); Null(Get(line, "productDisplayName")); Active(Get(line, "productIsActive"));
            lines.Add(new GoodsReceiptLineDto(id, lineNo, product, Decimal(Get(line, "quantity"), GoodsReceiptContractLimits.MaximumQuantity, true),
                Decimal(Get(line, "unitCost"), GoodsReceiptContractLimits.MaximumUnitCost)));
        }
        return new GoodsReceiptDto(receiptId, number, date, warehouse, employee, "Draft", note, null, version, lines);
    }

    private static void Shape(JsonElement value, string required, string optional = "")
    {
        var names = required.Split(','); var permitted = new HashSet<string>(names, StringComparer.Ordinal);
        foreach (var name in optional.Split(',')) if (name.Length != 0) permitted.Add(name);
        Require(value.ValueKind == JsonValueKind.Object);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            Require(seen.Add(property.Name) && permitted.Contains(property.Name));
        Require(names.All(seen.Contains));
    }
    private static JsonElement Lines(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() is >= 1 and <= 200);
        return value;
    }
    private static string Text(JsonElement token, int maximum)
    {
        Require(token.ValueKind == JsonValueKind.String); var value = token.GetString()!;
        Require(value.Length <= maximum && value.IndexOf('\0') < 0); Utf8.GetByteCount(value); return value;
    }
    private static long Id(JsonElement token)
    {
        var value = Text(token, 19);
        Require(Regex.IsMatch(value, "\\A[1-9][0-9]*\\z", RegexOptions.CultureInvariant) && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _));
        return long.Parse(value, CultureInfo.InvariantCulture);
    }
    private static int Int(JsonElement token)
    {
        Require(token.ValueKind == JsonValueKind.Number);
        Require(int.TryParse(token.GetRawText(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0); return value;
    }
    private static decimal Decimal(JsonElement token, decimal maximum, bool positive = false)
    {
        var value = Text(token, 64);
        Require(Regex.IsMatch(value, "\\A[0-9]+(?:\\.[0-9]{1,4})?\\z", RegexOptions.CultureInvariant));
        Require(decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) && number <= maximum && (positive ? number > 0 : number >= 0));
        return number;
    }
    private static string Number(JsonElement token, string prefix)
    {
        var value = Text(token, 12);
        Require(Regex.IsMatch(value, "\\A" + prefix + "[0-9]{10}\\z", RegexOptions.CultureInvariant) && value != prefix + "0000000000"); return value;
    }
    private static string Version(JsonElement token)
    {
        var value = Text(token, 12); var bytes = Convert.FromBase64String(value);
        Require(bytes.Length == 8 && Convert.ToBase64String(bytes) == value); return value;
    }
    private static DateTime Date(JsonElement token)
    {
        var value = Text(token, 10);
        Require(Regex.IsMatch(value, "\\A[0-9]{4}-[0-9]{2}-[0-9]{2}\\z", RegexOptions.CultureInvariant));
        Require(DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date != default); return date;
    }
    private static DateTime Utc(JsonElement token)
    {
        var value = Text(token, 28);
        Require(Regex.IsMatch(value, "\\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\\.[0-9]{1,7})?Z\\z", RegexOptions.CultureInvariant));
        Require(DateTime.TryParseExact(value, new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" }, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)); return date;
    }
    private static void Equal(JsonElement token, string expected) => Require(token.ValueKind == JsonValueKind.String && token.GetString() == expected);
    private static void Active(JsonElement token) => Require(token.ValueKind == JsonValueKind.True);
    private static void Null(JsonElement token) => Require(token.ValueKind == JsonValueKind.Null);
    private static void Require(bool condition) { if (!condition) throw Unavailable(); }
    private static WebOperationException Unavailable() => new WebOperationException("Operation.ResultUnavailable");
    private static bool CodecError(Exception error) => error is JsonException || error is ArgumentException ||
        error is InvalidOperationException || error is FormatException || error is OverflowException;
}
