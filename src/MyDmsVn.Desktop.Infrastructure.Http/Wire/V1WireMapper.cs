using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MyDmsVn.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MyDmsVn.Desktop.Infrastructure.Http.Wire
{
    /// <summary>Desktop-only v1 projection and response codec.
    /// The operation journal still owns its canonical JSON, not the DTO serializer.</summary>
    public static class V1WireMapper
    {
        public static SalesSaveWireDto SalesSave(SaveSalesReceiptRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return new SalesSaveWireDto(request.SaleDate, request.CustomerId, request.WarehouseId,
                request.EmployeeId, request.Note, Array.AsReadOnly(request.Lines.Select(
                    line => new SalesLineWireDto(line)).ToArray()), false, null);
        }

        public static SalesSaveWireDto SalesUpdate(UpdateSalesReceiptDraftRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return new SalesSaveWireDto(request.SaleDate, request.CustomerId, request.WarehouseId,
                request.EmployeeId, request.Note, Array.AsReadOnly(request.Lines.Select(
                    line => new SalesLineWireDto(line)).ToArray()), true, request.ExpectedVersion);
        }

        public static ReceiptSaveWireDto ReceiptSave(SaveGoodsReceiptRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return new ReceiptSaveWireDto(request.ReceiptDate, request.WarehouseId, request.EmployeeId,
                request.Note, Array.AsReadOnly(request.Lines.Select(line => new ReceiptLineWireDto(line))
                    .ToArray()), false, null);
        }

        public static ReceiptSaveWireDto ReceiptUpdate(UpdateGoodsReceiptDraftRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return new ReceiptSaveWireDto(request.ReceiptDate, request.WarehouseId, request.EmployeeId,
                request.Note, Array.AsReadOnly(request.Lines.Select(line => new ReceiptLineWireDto(line))
                    .ToArray()), true, request.ExpectedVersion);
        }

        public static ExpectedVersionWireDto ExpectedVersion(string version) => new ExpectedVersionWireDto(version);

        public static string SerializeRequest(object body) =>
            JsonConvert.SerializeObject(body, ApiJson.CreateSerializerSettings());

        // Keep DOM materialization at the existing operation canonicalization boundary only.
        internal static JObject CanonicalSource(object body) =>
            body as JObject ?? JObject.FromObject(body, JsonSerializer.Create(ApiJson.CreateSerializerSettings()));

        public static ApiResponse<T> DecodeResponse<T>(string json, ApiStatusCode status, bool httpSuccess)
        {
            var receipt = typeof(T) == typeof(GoodsReceiptDto) ||
                typeof(T) == typeof(PagedResult<GoodsReceiptDto>);
            var strictObject = typeof(T) == typeof(JObject) && httpSuccess;
            // Parse actual date properties only when mapping their typed contracts.
            // Otherwise an ISO-looking note/name becomes a DateTime before mapping,
            // while the retained journal's strict reader keeps that same value as text.
            var preserveDateText = receipt || typeof(T) == typeof(SalesReceiptDto) ||
                typeof(T) == typeof(SalesReceiptListPage) ||
                typeof(T) == typeof(PostSalesReceiptResponse) ||
                typeof(T) == typeof(PostGoodsReceiptResponse);
            var envelope = ReadEnvelope(json, receipt || strictObject, preserveDateText);
            if (strictObject && (envelope.Count != 1 || !(envelope["data"] is JObject)))
                throw new JsonSerializationException("Expected exactly one object data member.");
            return DecodeResponse<T>(envelope, status);
        }

        // Journal outcomes have already passed the strict JSON parse of the outer status.
        // Map that token directly; do not format it into JSON and parse it again.
        public static ApiResponse<T> DecodeResponse<T>(JObject envelope, ApiStatusCode status)
        {
            if (status == ApiStatusCode.Unspecified)
                throw new ArgumentOutOfRangeException(nameof(status));
            var serializer = JsonSerializer.Create(ApiJson.CreateSerializerSettings());
            var data = envelope.GetValue("data", StringComparison.OrdinalIgnoreCase);
            var error = envelope.GetValue("error", StringComparison.OrdinalIgnoreCase);
            if ((data == null) == (error == null))
                throw new JsonSerializationException("An API response must contain exactly one of data or error.");
            if (data != null)
            {
                if (data.Type == JTokenType.Null)
                    throw new JsonSerializationException("Response data cannot be null.");
                if (typeof(T) == typeof(GoodsReceiptDto))
                    return ApiResponse<T>.Success((T)(object)data.ToObject<ReceiptResponseWireDto>(serializer)!.ToContract());
                if (typeof(T) == typeof(PagedResult<GoodsReceiptDto>))
                {
                    var page = data.ToObject<PagedResult<ReceiptResponseWireDto>>(serializer)!;
                    var mapped = new PagedResult<GoodsReceiptDto>(page.Items.Select(item => item.ToContract()),
                        page.PageNumber, page.PageSize, page.TotalCount);
                    return ApiResponse<T>.Success((T)(object)mapped);
                }
                if (typeof(T) == typeof(UnitResponse))
                {
                    if (!(data is JObject unit) || unit.Count != 0)
                        throw new JsonSerializationException("Expected empty unit response data.");
                    return ApiResponse<T>.Success((T)(object)UnitResponse.Value);
                }
                return ApiResponse<T>.Success(data.ToObject<T>(serializer)!);
            }

            if (error!.Type == JTokenType.Null)
                throw new JsonSerializationException("Response error cannot be null.");
            var detailsToken = error["details"];
            var details = detailsToken == null || detailsToken.Type == JTokenType.Null
                ? Array.Empty<ApiErrorDetail>()
                : detailsToken.ToObject<IReadOnlyList<ApiErrorDetail>>(serializer) ?? Array.Empty<ApiErrorDetail>();
            return ApiResponse<T>.Failure(new ApiError(status, error.Value<string>("code")!,
                error.Value<string>("message")!, details, error.Value<string>("correlationId")));
        }

        internal static JObject ReadEnvelope(string json, bool strict, bool preserveDateText = false)
        {
            using (var text = new StringReader(json))
            using (var reader = new JsonTextReader(text)
            {
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = strict || preserveDateText ? DateParseHandling.None : DateParseHandling.DateTime,
                DateTimeZoneHandling = DateTimeZoneHandling.Utc,
                MaxDepth = 64,
            })
            {
                var result = JObject.Load(reader, new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = strict
                        ? DuplicatePropertyNameHandling.Error : DuplicatePropertyNameHandling.Replace,
                });
                while (reader.Read())
                {
                    if (strict || reader.TokenType != JsonToken.Comment)
                        throw new JsonSerializationException("Unexpected trailing JSON payload.");
                }
                return result;
            }
        }
    }
}
