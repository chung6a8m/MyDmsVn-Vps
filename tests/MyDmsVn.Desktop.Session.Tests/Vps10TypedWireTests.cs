using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Infrastructure.Http;
using MyDmsVn.Desktop.Infrastructure.Http.Wire;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Desktop.Session.Tests
{
    public sealed class Vps10TypedWireTests
    {
        [Theory]
        [InlineData("vi-VN")]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("")]
        public void Six_typed_mutations_match_frozen_wire_and_independent_fingerprints(string cultureName)
        {
            InCulture(cultureName, () =>
            {
                var vectors = HttpDesktopWire.ParseObjectPreservingDates(Read("web4-v1/fingerprint-vectors.json"));
                var matched = 0;
                foreach (var vector in vectors["vectors"]!.Take(6))
                {
                    var intent = (JObject)vector["intent"]!;
                    var body = (JObject)intent["body"]!;
                    var kind = (string)intent["operationKind"]!;
                    var target = (long?)intent["requestTargetId"];
                    var typed = ToTyped(kind, target, body);
                    Assert.Equal(body.ToString(Formatting.None), V1WireMapper.SerializeRequest(typed));
                    Assert.Equal((string)vector["sha256"]!,
                        HttpDesktopOperationFingerprint.Compute(kind, target, typed));
                    matched++;
                }
                Assert.Equal(6, matched);
            });
        }

        [Theory]
        [InlineData("vi-VN")]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("")]
        public void Frozen_live_and_retained_receipt_and_sales_values_match(string cultureName)
        {
            InCulture(cultureName, () =>
            {
                var receiptJson = Read("web3-v1/receipt-get-posted-response.json");
                var receipt = V1WireMapper.DecodeResponse<GoodsReceiptDto>(receiptJson, ApiStatusCode.Ok, true).Data!;
                var replay = V1WireMapper.DecodeResponse<GoodsReceiptDto>(
                    HttpDesktopWire.ParseObjectPreservingDates(receiptJson), ApiStatusCode.Ok).Data!;
                Assert.Equal(receipt.Id, replay.Id);
                Assert.Equal(9007199254740993L, receipt.Id);
                Assert.Equal(9007199254740996L, receipt.Lines[0].Id);
                Assert.Equal(12.3456m, receipt.Lines[1].UnitCost);
                Assert.Equal(new DateTime(2024, 2, 29), receipt.ReceiptDate);
                Assert.Equal(DateTimeKind.Unspecified, receipt.ReceiptDate.Kind);
                Assert.Equal(new DateTime(2026, 11, 1, 2, 3, 4, DateTimeKind.Utc).AddTicks(1234567), receipt.PostedAtUtc!.Value);
                Assert.Equal(DateTimeKind.Utc, receipt.PostedAtUtc.Value.Kind);
                Assert.False(receipt.WarehouseIsActive);
                Assert.Equal(JsonConvert.SerializeObject(receipt, ApiJson.CreateSerializerSettings()),
                    JsonConvert.SerializeObject(replay, ApiJson.CreateSerializerSettings()));
                var page = V1WireMapper.DecodeResponse<PagedResult<GoodsReceiptDto>>(
                    Read("web3-v1/receipt-list-response.json"), ApiStatusCode.Ok, true).Data!;
                Assert.Equal(1L, page.TotalCount);
                Assert.Null(Assert.Single(page.Items).PostedAtUtc);
                Assert.Equal(receipt.Id, page.Items[0].Id);

                var salesJson = Read("web2-v1/sales-get-posted-response.json");
                var sales = V1WireMapper.DecodeResponse<SalesReceiptDto>(salesJson, ApiStatusCode.Ok, true).Data!;
                var salesReplay = V1WireMapper.DecodeResponse<SalesReceiptDto>(
                    HttpDesktopWire.ParseObjectPreservingDates(salesJson), ApiStatusCode.Ok).Data!;
                Assert.Equal(long.MaxValue, sales.SaleId);
                Assert.Equal(9007199254740993L, sales.Lines[0].SaleLineId);
                Assert.Equal(1.00000001m, sales.Lines[0].PostedUnitCost!.Value);
                Assert.Equal(-0.7500m, sales.GrossProfit!.Value);
                Assert.Equal(sales.Lines[0].ProductId, sales.Lines[1].ProductId);
                Assert.True(sales.Lines[2].IsGift);
                Assert.Equal(JsonConvert.SerializeObject(sales, ApiJson.CreateSerializerSettings()),
                    JsonConvert.SerializeObject(salesReplay, ApiJson.CreateSerializerSettings()));
            });
        }

        [Theory]
        [InlineData("{\"data\":{},\"data\":{}}")]
        [InlineData("{\"data\":{}} {}")]
        [InlineData("{\"data\":null}")]
        [InlineData("{\"data\":{},\"error\":{}}")]
        public void Receipt_rejects_ambiguous_or_malformed_envelope(string json)
        {
            Assert.ThrowsAny<JsonException>(() =>
                V1WireMapper.DecodeResponse<GoodsReceiptDto>(json, ApiStatusCode.Ok, true));
        }

        [Fact]
        public void Explicit_null_note_calendar_and_version_presence_remain_distinct()
        {
            var date = new DateTime(2024, 2, 29, 23, 59, 0, DateTimeKind.Local);
            var lines = new[] { new SaveGoodsReceiptLineRequest(7, 1.2500m, 0m) };
            var create = V1WireMapper.ReceiptSave(new SaveGoodsReceiptRequest(date, 1, 2, null, lines));
            var update = V1WireMapper.ReceiptUpdate(new UpdateGoodsReceiptDraftRequest(
                1, "AAAAAAAAAAE=", date, 1, 2, null, lines));
            var createJson = JObject.Parse(V1WireMapper.SerializeRequest(create));
            var updateJson = JObject.Parse(V1WireMapper.SerializeRequest(update));
            Assert.Equal(JTokenType.Null, createJson["note"]!.Type);
            Assert.Null(createJson["expectedVersion"]);
            Assert.Equal("2024-02-29", (string?)createJson["receiptDate"]);
            Assert.Equal("1.2500", (string?)createJson["lines"]![0]!["quantity"]);
            Assert.Equal("AAAAAAAAAAE=", (string?)updateJson["expectedVersion"]);
            Assert.Null(updateJson["receiptId"]);
        }

        [Fact]
        public void Numeric_security_revision_is_not_rewritten_and_error_uses_transport_status()
        {
            var json = V1WireMapper.SerializeRequest(new { expectedRevision = 9007199254740993L });
            Assert.Equal("{\"expectedRevision\":9007199254740993}", json);
            var failure = V1WireMapper.DecodeResponse<GoodsReceiptDto>(
                "{\"error\":{\"code\":\"Receipt.Conflict\",\"message\":\"Conflict\",\"correlationId\":\"abc\",\"details\":[]}}",
                ApiStatusCode.Conflict, false);
            Assert.False(failure.IsSuccess);
            Assert.Equal(ApiStatusCode.Conflict, failure.Error!.Status);
            Assert.Equal("abc", failure.Error.CorrelationId);
        }

        [Theory]
        [InlineData("vi-VN")]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("")]
        public void Date_looking_business_text_is_identical_on_live_and_replay(string cultureName)
        {
            InCulture(cultureName, () =>
            {
                const string literal = "2026-11-01T09:03:04.1234567+07:00";
                var sales = HttpDesktopWire.ParseObjectPreservingDates(Read("web2-v1/sales-get-posted-response.json"));
                sales["data"]!["note"] = literal;
                sales["data"]!["lines"]![0]!["productDisplayName"] = literal;
                var live = V1WireMapper.DecodeResponse<SalesReceiptDto>(
                    sales.ToString(Formatting.None), ApiStatusCode.Ok, true).Data!;
                var replay = V1WireMapper.DecodeResponse<SalesReceiptDto>(sales, ApiStatusCode.Ok).Data!;
                Assert.Equal(literal, live.Note);
                Assert.Equal(literal, replay.Note);
                Assert.Equal(literal, live.Lines[0].ProductDisplayName);
                Assert.Equal(literal, replay.Lines[0].ProductDisplayName);
                Assert.Equal(new DateTime(2024, 2, 29), live.SaleDate);
                Assert.Equal(DateTimeKind.Unspecified, live.SaleDate.Kind);
                Assert.Equal(new DateTime(2026, 10, 31, 3, 0, 0, DateTimeKind.Utc), live.PostedAtUtc!.Value);
                Assert.Equal(DateTimeKind.Utc, live.PostedAtUtc.Value.Kind);

                var receipt = HttpDesktopWire.ParseObjectPreservingDates(Read("web3-v1/receipt-get-posted-response.json"));
                receipt["data"]!["note"] = literal;
                var receiptLive = V1WireMapper.DecodeResponse<GoodsReceiptDto>(
                    receipt.ToString(Formatting.None), ApiStatusCode.Ok, true).Data!;
                var receiptReplay = V1WireMapper.DecodeResponse<GoodsReceiptDto>(receipt, ApiStatusCode.Ok).Data!;
                Assert.Equal(literal, receiptLive.Note);
                Assert.Equal(literal, receiptReplay.Note);
                Assert.Equal(new DateTime(2026, 11, 1, 2, 3, 4, DateTimeKind.Utc).AddTicks(1234567),
                    receiptLive.PostedAtUtc!.Value);
                Assert.Equal(DateTimeKind.Utc, receiptLive.PostedAtUtc.Value.Kind);

                var page = HttpDesktopWire.ParseObjectPreservingDates(Read("web2-v1/sales-list-response.json"));
                page["data"]!["items"]![0]!["customerDisplayName"] = literal;
                var list = V1WireMapper.DecodeResponse<SalesReceiptListPage>(
                    page.ToString(Formatting.None), ApiStatusCode.Ok, true).Data!;
                Assert.Equal(literal, list.Items[0].CustomerDisplayName);
            });
        }

        private static object ToTyped(string kind, long? target, JObject body)
        {
            if (kind.EndsWith(".Post", StringComparison.Ordinal))
                return V1WireMapper.ExpectedVersion((string)body["expectedVersion"]!);
            var date = DateTime.ParseExact((string)(body["saleDate"] ?? body["receiptDate"])!,
                "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var warehouse = (int)body["warehouseId"]!;
            var employee = (int)body["employeeId"]!;
            var note = (string?)body["note"];
            var version = (string?)body["expectedVersion"];
            if (kind.StartsWith("Sales.", StringComparison.Ordinal))
            {
                var lines = body["lines"]!.Select(line => new SaveSalesReceiptLineRequest(
                    (long)line["saleLineId"]!, (int)line["productId"]!, ParseDecimal(line["qty"]!),
                    ParseDecimal(line["unitPrice"]!), (bool)line["isGift"]!)).ToArray();
                var customer = (int)body["customerId"]!;
                return target.HasValue
                    ? (object)V1WireMapper.SalesUpdate(new UpdateSalesReceiptDraftRequest(
                        target.Value, version!, date, customer, warehouse, employee, note, lines))
                    : V1WireMapper.SalesSave(new SaveSalesReceiptRequest(date, customer, warehouse, employee, note, lines));
            }
            var receiptLines = body["lines"]!.Select(line => new SaveGoodsReceiptLineRequest(
                (int)line["productId"]!, ParseDecimal(line["quantity"]!), ParseDecimal(line["unitCost"]!))).ToArray();
            return target.HasValue
                ? (object)V1WireMapper.ReceiptUpdate(new UpdateGoodsReceiptDraftRequest(
                    target.Value, version!, date, warehouse, employee, note, receiptLines))
                : V1WireMapper.ReceiptSave(new SaveGoodsReceiptRequest(date, warehouse, employee, note, receiptLines));
        }

        private static decimal ParseDecimal(JToken token) =>
            decimal.Parse((string)token!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

        internal static string Read(string relative)
        {
            var root = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (root != null && !Directory.Exists(Path.Combine(root.FullName, "docs", "contracts")))
                root = root.Parent;
            if (root == null) throw new InvalidOperationException("Frozen contract corpus is missing.");
            return File.ReadAllText(Path.Combine(root.FullName, "docs", "contracts", relative));
        }

        private static void InCulture(string cultureName, Action action)
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            var previousUi = Thread.CurrentThread.CurrentUICulture;
            try
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                action();
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
                Thread.CurrentThread.CurrentUICulture = previousUi;
            }
        }
    }
}
