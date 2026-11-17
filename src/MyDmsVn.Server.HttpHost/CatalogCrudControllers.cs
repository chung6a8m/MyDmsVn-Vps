#if VPS05_STJ_INGRESS
using JsonBody = MyDmsVn.Server.HttpHost.WireObject;
#else
using JsonBody = Newtonsoft.Json.Linq.JObject;
#endif
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using ErrorOr;
using MediatR;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Catalog;

namespace MyDmsVn.Server.HttpHost;

// Each operation delegates authorization, validation, SQL transaction boundaries
// and business error codes to the existing Application handler/pipeline.
public abstract class CatalogCrudControllerBase : WireApiController
{
    protected CatalogCrudControllerBase(IMediator mediator, IRequestCorrelationAccessor correlation) : base(mediator, correlation) { }
    protected Task<HttpResponseMessage> List<T>(Func<CatalogListRequest, IRequest<ErrorOr<PagedResult<T>>>> query, CancellationToken token) =>
        ExecuteDesktop<PagedResult<T>>(async () =>
        {
            await CatalogWireInput.EmptyBody(Request).ConfigureAwait(false);
            return await Mediator.Send(query(CatalogWireInput.List(Request)), token).ConfigureAwait(false);
        }, result => result);
    protected Task<HttpResponseMessage> Get<T>(string id, Func<int, IRequest<ErrorOr<T>>> query, CancellationToken token) =>
        ExecuteDesktop<T>(async () =>
        {
            CatalogWireInput.NoQuery(Request);
            await CatalogWireInput.EmptyBody(Request).ConfigureAwait(false);
            return await Mediator.Send(query(CatalogWireInput.Id(id)), token).ConfigureAwait(false);
        }, result => result!);
    protected Task<HttpResponseMessage> Create<T, TRequest>(Func<JsonBody, TRequest> decode,
        Func<TRequest, IRequest<ErrorOr<T>>> command, CancellationToken token) =>
        ExecuteDesktop<T>(async () =>
        {
            var body = await CatalogWireInput.Body(Request).ConfigureAwait(false);
            return await Mediator.Send(command(decode(body)), token).ConfigureAwait(false);
        }, result => result!);
    protected Task<HttpResponseMessage> Update<T, TRequest>(string id, Func<JsonBody, TRequest> decode,
        Func<int, TRequest, IRequest<ErrorOr<T>>> command, CancellationToken token) =>
        ExecuteDesktop<T>(async () =>
        {
            var key = CatalogWireInput.Id(id);
            var body = await CatalogWireInput.Body(Request).ConfigureAwait(false);
            return await Mediator.Send(command(key, decode(body)), token).ConfigureAwait(false);
        }, result => result!);
    protected Task<HttpResponseMessage> Active(string id, Func<int, bool, IRequest<ErrorOr<UnitResponse>>> command, CancellationToken token) =>
        ExecuteDesktop<UnitResponse>(async () =>
        {
            var key = CatalogWireInput.Id(id);
            var body = await CatalogWireInput.Body(Request).ConfigureAwait(false);
            return await Mediator.Send(command(key, CatalogWireInput.Active(body)), token).ConfigureAwait(false);
        }, result => result);
}

[RoutePrefix("api/v1/catalog/products")]
public sealed class ProductsController : CatalogCrudControllerBase
{
    public ProductsController(IMediator mediator, IRequestCorrelationAccessor correlation) : base(mediator, correlation) { }
    [HttpGet, Route("")]
    public Task<HttpResponseMessage> List(CancellationToken token) => List<ProductDto>(request => new ListProductsQuery(request), token);
    [HttpGet, Route("{id:int}")]
    public Task<HttpResponseMessage> Get(string id, CancellationToken token) => Get<ProductDto>(id, key => new GetProductByIdQuery(key), token);
    [HttpPost, Route("")]
    public Task<HttpResponseMessage> Create(CancellationToken token) => Create<ProductDto, SaveProductRequest>(CatalogWireInput.Product, request => new CreateProductCommand(request), token);
    [HttpPut, Route("{id:int}")]
    public Task<HttpResponseMessage> Update(string id, CancellationToken token) => Update<ProductDto, SaveProductRequest>(id, CatalogWireInput.Product, (key, request) => new UpdateProductCommand(key, request), token);
    [HttpPut, Route("{id:int}/active")]
    public Task<HttpResponseMessage> SetActive(string id, CancellationToken token) => Active(id, (key, active) => new SetProductActiveCommand(key, active), token);
}

[RoutePrefix("api/v1/catalog/warehouses")]
public sealed class WarehousesController : CatalogCrudControllerBase
{
    public WarehousesController(IMediator mediator, IRequestCorrelationAccessor correlation) : base(mediator, correlation) { }
    [HttpGet, Route("")]
    public Task<HttpResponseMessage> List(CancellationToken token) => List<WarehouseDto>(request => new ListWarehousesQuery(request), token);
    [HttpGet, Route("{id:int}")]
    public Task<HttpResponseMessage> Get(string id, CancellationToken token) => Get<WarehouseDto>(id, key => new GetWarehouseByIdQuery(key), token);
    [HttpPost, Route("")]
    public Task<HttpResponseMessage> Create(CancellationToken token) => Create<WarehouseDto, SaveWarehouseRequest>(CatalogWireInput.Warehouse, request => new CreateWarehouseCommand(request), token);
    [HttpPut, Route("{id:int}")]
    public Task<HttpResponseMessage> Update(string id, CancellationToken token) => Update<WarehouseDto, SaveWarehouseRequest>(id, CatalogWireInput.Warehouse, (key, request) => new UpdateWarehouseCommand(key, request), token);
    [HttpPut, Route("{id:int}/active")]
    public Task<HttpResponseMessage> SetActive(string id, CancellationToken token) => Active(id, (key, active) => new SetWarehouseActiveCommand(key, active), token);
}

[RoutePrefix("api/v1/catalog/employees")]
public sealed class EmployeesController : CatalogCrudControllerBase
{
    public EmployeesController(IMediator mediator, IRequestCorrelationAccessor correlation) : base(mediator, correlation) { }
    [HttpGet, Route("")]
    public Task<HttpResponseMessage> List(CancellationToken token) => List<EmployeeDto>(request => new ListEmployeesQuery(request), token);
    [HttpGet, Route("{id:int}")]
    public Task<HttpResponseMessage> Get(string id, CancellationToken token) => Get<EmployeeDto>(id, key => new GetEmployeeByIdQuery(key), token);
    [HttpPost, Route("")]
    public Task<HttpResponseMessage> Create(CancellationToken token) => Create<EmployeeDto, SaveEmployeeRequest>(CatalogWireInput.Employee, request => new CreateEmployeeCommand(request), token);
    [HttpPut, Route("{id:int}")]
    public Task<HttpResponseMessage> Update(string id, CancellationToken token) => Update<EmployeeDto, SaveEmployeeRequest>(id, CatalogWireInput.Employee, (key, request) => new UpdateEmployeeCommand(key, request), token);
    [HttpPut, Route("{id:int}/active")]
    public Task<HttpResponseMessage> SetActive(string id, CancellationToken token) => Active(id, (key, active) => new SetEmployeeActiveCommand(key, active), token);
}

[RoutePrefix("api/v1/catalog/customers")]
public sealed class CustomersController : CatalogCrudControllerBase
{
    public CustomersController(IMediator mediator, IRequestCorrelationAccessor correlation) : base(mediator, correlation) { }
    [HttpGet, Route("")]
    public Task<HttpResponseMessage> List(CancellationToken token) => List<CustomerDto>(request => new ListCustomersQuery(request), token);
    [HttpGet, Route("{id:int}")]
    public Task<HttpResponseMessage> Get(string id, CancellationToken token) => Get<CustomerDto>(id, key => new GetCustomerByIdQuery(key), token);
    [HttpPost, Route("")]
    public Task<HttpResponseMessage> Create(CancellationToken token) => Create<CustomerDto, SaveCustomerRequest>(CatalogWireInput.Customer, request => new CreateCustomerCommand(request), token);
    [HttpPut, Route("{id:int}")]
    public Task<HttpResponseMessage> Update(string id, CancellationToken token) => Update<CustomerDto, SaveCustomerRequest>(id, CatalogWireInput.Customer, (key, request) => new UpdateCustomerCommand(key, request), token);
    [HttpPut, Route("{id:int}/active")]
    public Task<HttpResponseMessage> SetActive(string id, CancellationToken token) => Active(id, (key, active) => new SetCustomerActiveCommand(key, active), token);
}
