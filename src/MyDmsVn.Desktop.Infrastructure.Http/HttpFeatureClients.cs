using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Desktop.Application;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    public sealed class HttpIdentityApiClient : IIdentityApiClient
    {
        private readonly HttpDesktopTransport _transport;
        public HttpIdentityApiClient(HttpDesktopTransport transport) { _transport = transport; }
        public Task<ApiResponse<CurrentUserDto>> LoginAsync(LoginRequest request, CancellationToken token) =>
            _transport.LoginAsync(request, token);
        public Task<ApiResponse<CurrentUserDto>> GetCurrentUserAsync(CancellationToken token) =>
            _transport.GetCurrentUserAsync(token);
    }

    public sealed class HttpFoundationApiClient : IFoundationApiClient
    {
        private readonly HttpDesktopTransport _transport;
        public HttpFoundationApiClient(HttpDesktopTransport transport) { _transport = transport; }
        public Task<ApiResponse<FoundationStatus>> GetStatusAsync(FoundationStatusRequest request, CancellationToken token) =>
            _transport.SendAsync<FoundationStatus>(HttpMethod.Get,
                "/api/v1/foundation/status" +
                HttpDesktopWire.Query(("clientName", request.ClientName)), null, token);
    }

    public sealed class HttpPermissionApiClient : IPermissionApiClient
    {
        private readonly HttpDesktopTransport _transport;
        public HttpPermissionApiClient(HttpDesktopTransport transport) { _transport = transport; }
        public Task<ApiResponse<PermissionDecisionDto>> CheckAsync(string key, CancellationToken token) =>
            _transport.SendAsync<PermissionDecisionDto>(HttpMethod.Get,
                "/api/v1/security/permissions/check" +
                HttpDesktopWire.Query(("key", key)), null, token);
    }

    public sealed class HttpPermissionCatalogApiClient : IPermissionCatalogApiClient
    {
        private readonly HttpDesktopTransport _transport;
        private readonly long? _expectedVersion;
        public HttpPermissionCatalogApiClient(HttpDesktopTransport transport) : this(transport, null) { }
        private HttpPermissionCatalogApiClient(HttpDesktopTransport transport, long? expectedVersion)
        { _transport = transport; _expectedVersion = expectedVersion; }

        public IPermissionCatalogApiClient ForSession(long expectedSessionVersion)
        {
            if (_expectedVersion.HasValue)
            {
                if (_expectedVersion.Value != expectedSessionVersion)
                    throw new InvalidOperationException("A permission client cannot be rebound.");
                return this;
            }
            return new HttpPermissionCatalogApiClient(_transport, expectedSessionVersion);
        }

        public Task<ApiResponse<PermissionCatalogDto>> GetCatalogAsync(CancellationToken token) =>
            _transport.SendAsync<PermissionCatalogDto>(HttpMethod.Get,
                "/api/v1/security/permissions/catalog", null, token, _expectedVersion);
    }

    public sealed class HttpSecurityAdministrationApiClient : ISecurityAdministrationApiClient
    {
        private readonly HttpDesktopTransport _transport;
        private readonly long? _expectedVersion;
        public HttpSecurityAdministrationApiClient(HttpDesktopTransport transport) : this(transport, null) { }
        private HttpSecurityAdministrationApiClient(HttpDesktopTransport transport, long? expectedVersion)
        { _transport = transport; _expectedVersion = expectedVersion; }

        public ISecurityAdministrationApiClient ForSession(long expectedSessionVersion)
        {
            if (_expectedVersion.HasValue)
            {
                if (_expectedVersion.Value != expectedSessionVersion)
                    throw new InvalidOperationException("A security client cannot be rebound.");
                return this;
            }
            return new HttpSecurityAdministrationApiClient(_transport, expectedSessionVersion);
        }

        public Task<ApiResponse<IReadOnlyList<SecurityUserDto>>> ListUsersAsync(CancellationToken token) =>
            _transport.SendAsync<IReadOnlyList<SecurityUserDto>>(HttpMethod.Get,
                "/api/v1/security/users", null, token, _expectedVersion);

        public Task<ApiResponse<IReadOnlyList<SecurityRoleDto>>> ListRolesAsync(CancellationToken token) =>
            _transport.SendAsync<IReadOnlyList<SecurityRoleDto>>(HttpMethod.Get,
                "/api/v1/security/roles", null, token, _expectedVersion);

        public Task<ApiResponse<SecurityAdministrationResultDto>> MutateAsync(
            SecurityAdministrationMutationRequest request, CancellationToken token) =>
            _transport.SendAsync<SecurityAdministrationResultDto>(HttpMethod.Post,
                "/api/v1/security/mutations", request, token, _expectedVersion);

        public Task<ApiResponse<PermissionAssignmentImportPreviewDto>> PreviewPermissionImportAsync(
            PermissionImportTargetTypeDto targetType, int targetId, string json, CancellationToken token) =>
            _transport.SendAsync<PermissionAssignmentImportPreviewDto>(HttpMethod.Post,
                "/api/v1/security/import/preview",
                new { targetType = (int)targetType, targetId, json }, token, _expectedVersion);

        public Task<ApiResponse<PermissionAssignmentImportResultDto>> ApplyPermissionImportAsync(
            PermissionImportTargetTypeDto targetType, int targetId,
            PermissionAssignmentImportRequest request, CancellationToken token) =>
            _transport.SendAsync<PermissionAssignmentImportResultDto>(HttpMethod.Post,
                "/api/v1/security/import/apply",
                new { targetType = (int)targetType, targetId, json = request.Json,
                    expectedTargetRevision = request.ExpectedTargetRevision,
                    expectedCatalogDigest = request.ExpectedCatalogDigest }, token, _expectedVersion);
    }

    public sealed class HttpSalesReceiptApiClient : ISalesReceiptApiClient
    {
        private readonly HttpDesktopTransport _transport;
        private readonly HttpDesktopOperations _operations;
        private readonly long? _expectedVersion;

        public HttpSalesReceiptApiClient(HttpDesktopTransport transport, HttpDesktopOperations operations)
            : this(transport, operations, null) { }

        private HttpSalesReceiptApiClient(HttpDesktopTransport transport,
            HttpDesktopOperations operations, long? expectedVersion)
        {
            _transport = transport;
            _operations = operations;
            _expectedVersion = expectedVersion;
        }

        public ISalesReceiptApiClient ForSession(long expectedSessionVersion)
        {
            if (_expectedVersion.HasValue)
            {
                if (_expectedVersion.Value != expectedSessionVersion)
                    throw new InvalidOperationException("A sales client cannot be rebound.");
                return this;
            }
            return new HttpSalesReceiptApiClient(_transport, _operations, expectedSessionVersion);
        }

        public Task<ApiResponse<SalesReceiptListPage>> ListAsync(SalesReceiptListRequest request, CancellationToken token) =>
            _transport.SendAsync<SalesReceiptListPage>(HttpMethod.Get,
                "/api/v1/sales-receipts" + HttpDesktopWire.SalesList(request),
                null, token, _expectedVersion);

        public Task<ApiResponse<SalesReceiptDto>> GetAsync(long id, CancellationToken token) =>
            _transport.SendAsync<SalesReceiptDto>(HttpMethod.Get,
                "/api/v1/sales-receipts/" + Id(id), null, token, _expectedVersion);

        public Task<ApiResponse<SalesReceiptDto>> CreateDraftAsync(SaveSalesReceiptRequest request, CancellationToken token) =>
            _operations.SendAsync<SalesReceiptDto>("Sales.Create", null, HttpMethod.Post,
                "/api/v1/sales-receipts", HttpDesktopWire.SalesSave(request), _expectedVersion, token);

        public Task<ApiResponse<SalesReceiptDto>> UpdateDraftAsync(UpdateSalesReceiptDraftRequest request, CancellationToken token) =>
            _operations.SendAsync<SalesReceiptDto>("Sales.Update", request.SaleId, HttpMethod.Put,
                "/api/v1/sales-receipts/" + Id(request.SaleId) + "/draft",
                HttpDesktopWire.SalesUpdate(request), _expectedVersion, token);

        public Task<ApiResponse<PostSalesReceiptResponse>> PostAsync(PostSalesReceiptRequest request, CancellationToken token) =>
            _operations.SendAsync<PostSalesReceiptResponse>("Sales.Post", request.SaleId, HttpMethod.Post,
                "/api/v1/sales-receipts/" + Id(request.SaleId) + "/post",
                HttpDesktopWire.ExpectedVersion(request.ExpectedVersion), _expectedVersion, token);

        private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class HttpGoodsReceiptApiClient : IGoodsReceiptApiClient
    {
        private readonly HttpDesktopTransport _transport;
        private readonly HttpDesktopOperations _operations;
        public HttpGoodsReceiptApiClient(HttpDesktopTransport transport, HttpDesktopOperations operations)
        { _transport = transport; _operations = operations; }

        public Task<ApiResponse<GoodsReceiptDto>> CreateDraftAsync(SaveGoodsReceiptRequest request, CancellationToken token) =>
            _operations.SendAsync<GoodsReceiptDto>("Receipt.Create", null, HttpMethod.Post,
                "/api/v1/goods-receipts", HttpDesktopWire.ReceiptSave(request), null, token);

        public Task<ApiResponse<GoodsReceiptDto>> UpdateDraftAsync(UpdateGoodsReceiptDraftRequest request, CancellationToken token) =>
            _operations.SendAsync<GoodsReceiptDto>("Receipt.Update", request.ReceiptId, HttpMethod.Put,
                "/api/v1/goods-receipts/" + Id(request.ReceiptId) + "/draft",
                HttpDesktopWire.ReceiptUpdate(request), null, token);

        public Task<ApiResponse<PostGoodsReceiptResponse>> PostAsync(PostGoodsReceiptRequest request, CancellationToken token) =>
            _operations.SendAsync<PostGoodsReceiptResponse>("Receipt.Post", request.ReceiptId, HttpMethod.Post,
                "/api/v1/goods-receipts/" + Id(request.ReceiptId) + "/post",
                HttpDesktopWire.ExpectedVersion(request.ExpectedVersion), null, token);

        public Task<ApiResponse<GoodsReceiptDto>> GetAsync(long id, CancellationToken token) =>
            _transport.SendAsync<GoodsReceiptDto>(HttpMethod.Get,
                "/api/v1/goods-receipts/" + Id(id), null, token);

        public Task<ApiResponse<PagedResult<GoodsReceiptDto>>> ListAsync(GoodsReceiptListRequest request, CancellationToken token) =>
            _transport.SendAsync<PagedResult<GoodsReceiptDto>>(HttpMethod.Get,
                "/api/v1/goods-receipts" + HttpDesktopWire.ReceiptList(request), null, token);

        private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);
    }

    public sealed class HttpInventoryApiClient : IInventoryApiClient
    {
        private readonly HttpDesktopTransport _transport;
        public HttpInventoryApiClient(HttpDesktopTransport transport) { _transport = transport; }

        public Task<ApiResponse<PagedResult<StockBalanceRow>>> GetStockBalancesAsync(StockBalanceQuery request, CancellationToken token) =>
            _transport.SendAsync<PagedResult<StockBalanceRow>>(HttpMethod.Get,
                "/api/v1/inventory/balances" + HttpDesktopWire.Balances(request), null, token);

        public Task<ApiResponse<PagedResult<StockCardRow>>> GetStockCardAsync(StockCardQuery request, CancellationToken token) =>
            _transport.SendAsync<PagedResult<StockCardRow>>(HttpMethod.Get,
                "/api/v1/inventory/stock-card" + HttpDesktopWire.StockCard(request), null, token);
    }
}
