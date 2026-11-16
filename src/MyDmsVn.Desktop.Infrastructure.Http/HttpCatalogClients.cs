using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    public abstract class HttpCatalogClientBase<TDto, TSave>
    {
        private readonly HttpDesktopTransport _transport;
        private readonly string _route;

        protected HttpCatalogClientBase(HttpDesktopTransport transport, string plural)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _route = "/api/v1/catalog/" + plural;
        }

        protected Task<ApiResponse<PagedResult<TDto>>> List(CatalogListRequest request, CancellationToken token) =>
            _transport.SendAsync<PagedResult<TDto>>(HttpMethod.Get,
                _route + HttpDesktopWire.CatalogList(request), null, token);

        protected Task<ApiResponse<TDto>> Get(int id, CancellationToken token) =>
            _transport.SendAsync<TDto>(HttpMethod.Get, _route + "/" +
                id.ToString(CultureInfo.InvariantCulture), null, token);

        protected Task<ApiResponse<TDto>> Create(TSave request, CancellationToken token) =>
            _transport.SendAsync<TDto>(HttpMethod.Post, _route, request, token);

        protected Task<ApiResponse<TDto>> Update(int id, TSave request, CancellationToken token) =>
            _transport.SendAsync<TDto>(HttpMethod.Put, _route + "/" +
                id.ToString(CultureInfo.InvariantCulture), request, token);

        protected Task<ApiResponse<UnitResponse>> SetActive(int id, bool isActive, CancellationToken token) =>
            _transport.SendAsync<UnitResponse>(HttpMethod.Put, _route + "/" +
                id.ToString(CultureInfo.InvariantCulture) + "/active",
                new { isActive }, token);

        protected Task<ApiResponse<IReadOnlyList<CatalogLookupDto>>> Lookup(CatalogLookupRequest request, CancellationToken token) =>
            _transport.SendAsync<IReadOnlyList<CatalogLookupDto>>(HttpMethod.Get,
                _route + "/lookup" + HttpDesktopWire.Lookup(request), null, token);
    }

    public sealed class HttpProductApiClient : HttpCatalogClientBase<ProductDto, SaveProductRequest>, IProductApiClient
    {
        public HttpProductApiClient(HttpDesktopTransport transport) : base(transport, "products") { }
        public Task<ApiResponse<PagedResult<ProductDto>>> ListAsync(CatalogListRequest request, CancellationToken token) => List(request, token);
        public Task<ApiResponse<ProductDto>> GetAsync(int id, CancellationToken token) => Get(id, token);
        public Task<ApiResponse<ProductDto>> CreateAsync(SaveProductRequest request, CancellationToken token) => Create(request, token);
        public Task<ApiResponse<ProductDto>> UpdateAsync(int id, SaveProductRequest request, CancellationToken token) => Update(id, request, token);
        public Task<ApiResponse<UnitResponse>> SetActiveAsync(int id, bool isActive, CancellationToken token) => SetActive(id, isActive, token);
        public Task<ApiResponse<IReadOnlyList<CatalogLookupDto>>> LookupAsync(CatalogLookupRequest request, CancellationToken token) => Lookup(request, token);
    }

    public sealed class HttpWarehouseApiClient : HttpCatalogClientBase<WarehouseDto, SaveWarehouseRequest>, IWarehouseApiClient
    {
        public HttpWarehouseApiClient(HttpDesktopTransport transport) : base(transport, "warehouses") { }
        public Task<ApiResponse<PagedResult<WarehouseDto>>> ListAsync(CatalogListRequest request, CancellationToken token) => List(request, token);
        public Task<ApiResponse<WarehouseDto>> GetAsync(int id, CancellationToken token) => Get(id, token);
        public Task<ApiResponse<WarehouseDto>> CreateAsync(SaveWarehouseRequest request, CancellationToken token) => Create(request, token);
        public Task<ApiResponse<WarehouseDto>> UpdateAsync(int id, SaveWarehouseRequest request, CancellationToken token) => Update(id, request, token);
        public Task<ApiResponse<UnitResponse>> SetActiveAsync(int id, bool isActive, CancellationToken token) => SetActive(id, isActive, token);
        public Task<ApiResponse<IReadOnlyList<CatalogLookupDto>>> LookupAsync(CatalogLookupRequest request, CancellationToken token) => Lookup(request, token);
    }

    public sealed class HttpEmployeeApiClient : HttpCatalogClientBase<EmployeeDto, SaveEmployeeRequest>, IEmployeeApiClient
    {
        public HttpEmployeeApiClient(HttpDesktopTransport transport) : base(transport, "employees") { }
        public Task<ApiResponse<PagedResult<EmployeeDto>>> ListAsync(CatalogListRequest request, CancellationToken token) => List(request, token);
        public Task<ApiResponse<EmployeeDto>> GetAsync(int id, CancellationToken token) => Get(id, token);
        public Task<ApiResponse<EmployeeDto>> CreateAsync(SaveEmployeeRequest request, CancellationToken token) => Create(request, token);
        public Task<ApiResponse<EmployeeDto>> UpdateAsync(int id, SaveEmployeeRequest request, CancellationToken token) => Update(id, request, token);
        public Task<ApiResponse<UnitResponse>> SetActiveAsync(int id, bool isActive, CancellationToken token) => SetActive(id, isActive, token);
        public Task<ApiResponse<IReadOnlyList<CatalogLookupDto>>> LookupAsync(CatalogLookupRequest request, CancellationToken token) => Lookup(request, token);
    }

    public sealed class HttpCustomerApiClient : HttpCatalogClientBase<CustomerDto, SaveCustomerRequest>, ICustomerApiClient
    {
        public HttpCustomerApiClient(HttpDesktopTransport transport) : base(transport, "customers") { }
        public Task<ApiResponse<PagedResult<CustomerDto>>> ListAsync(CatalogListRequest request, CancellationToken token) => List(request, token);
        public Task<ApiResponse<CustomerDto>> GetAsync(int id, CancellationToken token) => Get(id, token);
        public Task<ApiResponse<CustomerDto>> CreateAsync(SaveCustomerRequest request, CancellationToken token) => Create(request, token);
        public Task<ApiResponse<CustomerDto>> UpdateAsync(int id, SaveCustomerRequest request, CancellationToken token) => Update(id, request, token);
        public Task<ApiResponse<UnitResponse>> SetActiveAsync(int id, bool isActive, CancellationToken token) => SetActive(id, isActive, token);
        public Task<ApiResponse<IReadOnlyList<CatalogLookupDto>>> LookupAsync(CatalogLookupRequest request, CancellationToken token) => Lookup(request, token);
    }
}
