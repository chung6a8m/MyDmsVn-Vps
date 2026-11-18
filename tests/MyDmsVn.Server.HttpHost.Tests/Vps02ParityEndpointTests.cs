using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Catalog;
using MyDmsVn.Server.Application.Security;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Server.HttpHost.Tests;

public sealed class Vps02ParityEndpointTests
{
    [Theory]
    [InlineData("products", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":\"kg\"}")]
    [InlineData("warehouses", "{\"code\":\"W1\",\"name\":\"Warehouse\",\"address\":null}")]
    [InlineData("employees", "{\"code\":\"E1\",\"name\":\"Employee\",\"phone\":null,\"userId\":1}")]
    [InlineData("customers", "{\"code\":\"C1\",\"name\":\"Customer\",\"address\":null,\"phone\":null,\"taxCode\":null}")]
    public async Task All_catalog_commands_and_queries_dispatch_to_existing_handlers(string catalog, string body)
    {
        using var fixture = new DesktopParityFixture();
        foreach (var (verb, url, json) in new[]
        {
            ("GET", "/catalog/" + catalog + "?search=abc&pageNumber=2&pageSize=25&includeInactive=true", (string?)null),
            ("GET", "/catalog/" + catalog + "/1", (string?)null),
            ("POST", "/catalog/" + catalog, body),
            ("PUT", "/catalog/" + catalog + "/1", body),
            ("PUT", "/catalog/" + catalog + "/1/active", "{\"isActive\":false}"),
        })
        {
            using var response = await fixture.Send(verb, url, json);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(JObject.Parse(await response.Content.ReadAsStringAsync())["data"]);
        }
        Assert.Equal(5, fixture.State.Calls.Count);
        if (catalog == "products")
        {
            var list = Assert.Single(fixture.State.Calls.OfType<ListProductsQuery>());
            Assert.Equal(2, list.Request.PageNumber);
            Assert.Equal(25, list.Request.PageSize);
            Assert.True(list.Request.IncludeInactive);
        }
    }

    [Theory]
    [InlineData("products", "Catalog.Products.Read", "Catalog.Products.Write")]
    [InlineData("warehouses", "Catalog.Warehouses.Read", "Catalog.Warehouses.Write")]
    [InlineData("employees", "Catalog.Employees.Read", "Catalog.Employees.Write")]
    [InlineData("customers", "Catalog.Customers.Read", "Catalog.Customers.Write")]
    public async Task Catalog_read_and_write_permissions_are_independent(string catalog, string read, string write)
    {
        using var fixture = new DesktopParityFixture();
        fixture.State.Denied.Add(read);
        using var deniedRead = await fixture.Send("GET", "/catalog/" + catalog);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        using var allowedWrite = await fixture.Send("PUT", "/catalog/" + catalog + "/1/active", "{\"isActive\":true}");
        Assert.Equal(HttpStatusCode.OK, allowedWrite.StatusCode);
        fixture.State.Denied.Add(write);
        using var deniedWrite = await fixture.Send("PUT", "/catalog/" + catalog + "/1/active", "{\"isActive\":true}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
        Assert.Single(fixture.State.Calls);
    }

    [Theory]
    [InlineData("POST", "/catalog/products", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":\"kg\",\"admin\":true}")]
    [InlineData("POST", "/catalog/products", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":\"kg\",\"code\":\"P2\"}")]
    [InlineData("POST", "/catalog/products", "{'code':'P1','name':'Product','unit':'kg'}")]
    [InlineData("POST", "/catalog/products", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":\"kg\",}")]
    [InlineData("POST", "/catalog/products", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":1}")]
    [InlineData("PUT", "/catalog/products/1/active", "{\"isActive\":1}")]
    [InlineData("POST", "/catalog/employees", "{\"code\":\"E1\",\"name\":\"Employee\",\"userId\":\"1\"}")]
    [InlineData("POST", "/security/mutations", "{\"target\":0,\"operation\":0,\"password\":\"redacted\",\"isActive\":\"true\"}")]
    [InlineData("POST", "/security/import/apply", "{\"targetType\":0,\"targetId\":1,\"json\":\"{}\",\"expectedTargetRevision\":\"1\",\"expectedCatalogDigest\":\"digest\"}")]
    public async Task Unsafe_or_coerced_json_is_rejected_before_any_handler(string method, string route, string body)
    {
        using var fixture = new DesktopParityFixture();
        using var response = await fixture.Send(method, route, body);
        await Invalid(response);
        Assert.Empty(fixture.State.Calls);
    }

    [Theory]
    [InlineData("/catalog/products?pageNumber=0")]
    [InlineData("/catalog/products?pageSize=201")]
    [InlineData("/catalog/products?pageNumber=01")]
    [InlineData("/catalog/products?includeInactive=1")]
    [InlineData("/catalog/products?unknown=1")]
    [InlineData("/catalog/products?pageNumber=1&pageNumber=2")]
    [InlineData("/catalog/products/01")]
    [InlineData("/security/permissions/check?key=")]
    [InlineData("/security/permissions/check?key=%20")]
    [InlineData("/security/permissions/check?key=Catalog.Read&unknown=1")]
    [InlineData("/foundation/status?clientName=")]
    public async Task Invalid_path_or_query_does_not_dispatch(string path)
    {
        using var fixture = new DesktopParityFixture();
        using var response = await fixture.Send("GET", path);
        await Invalid(response);
        Assert.Empty(fixture.State.Calls);
    }

    [Fact]
    public async Task Security_mutation_roundtrips_expected_revisions_without_leaking_password()
    {
        using var fixture = new DesktopParityFixture();
        var body = "{\"target\":0,\"operation\":3,\"targetId\":7,\"expectedRevision\":9007199254740993," +
            "\"password\":\"test-secret\",\"correlationId\":\"11111111-1111-4111-8111-111111111111\"}";
        using var response = await fixture.Send("POST", "/security/mutations", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, (long)result["data"]!["assignmentRevision"]!);
        Assert.DoesNotContain("test-secret", result.ToString());
        var command = Assert.IsType<MutateSecurityAdministrationCommand>(Assert.Single(fixture.State.Calls));
        Assert.Equal(9007199254740993L, command.ExpectedRevision);
        Assert.Equal("test-secret", command.Password);
        Assert.Equal(Guid.Parse("11111111-1111-4111-8111-111111111111"), command.CorrelationId);
    }

    [Fact]
    public async Task Security_import_preview_and_apply_preserve_revision_and_data()
    {
        using var fixture = new DesktopParityFixture();
        var json = "{\"schemaVersion\":1,\"targetType\":\"Role\",\"targetId\":3,\"entries\":[]}";
        var escaped = Newtonsoft.Json.JsonConvert.ToString(json);
        using var preview = await fixture.Send("POST", "/security/import/preview", "{\"targetType\":1,\"targetId\":3,\"json\":" + escaped + "}");
        using var apply = await fixture.Send("POST", "/security/import/apply", "{\"targetType\":1,\"targetId\":3,\"json\":" + escaped +
            ",\"expectedTargetRevision\":9007199254740993,\"expectedCatalogDigest\":\"digest\"}");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, apply.StatusCode);
        Assert.Equal(json, Assert.IsType<PreviewPermissionAssignmentImportQuery>(fixture.State.Calls[0]).Json);
        Assert.Equal(9007199254740993L, Assert.IsType<ApplyPermissionAssignmentImportCommand>(fixture.State.Calls[1]).ExpectedTargetRevision);
    }

    [Fact]
    public async Task Permission_catalog_user_role_and_foundation_resolve_with_bearer()
    {
        using var fixture = new DesktopParityFixture();
        foreach (var path in new[] { "/security/permissions/catalog", "/security/users",
            "/security/roles", "/security/permissions/check?key=Catalog.Products.Read", "/foundation/status?clientName=Desktop" })
        {
            using var response = await fixture.Send("GET", path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(JObject.Parse(await response.Content.ReadAsStringAsync())["data"]);
        }
        Assert.Equal(5, fixture.State.Calls.Count);
        Assert.Equal("Catalog.Products.Read", Assert.IsType<CheckPermissionQuery>(fixture.State.Calls[3]).PermissionKey);
    }

    [Fact]
    public async Task Revoked_login_cannot_use_additive_routes()
    {
        using var fixture = new DesktopParityFixture();
        using var unauthorized = await fixture.Send("GET", "/catalog/products", authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        fixture.Store.Revoked = true;
        using var revoked = await fixture.Send("GET", "/security/users");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Empty(fixture.State.Calls);
    }

    [Fact]
    public async Task Feature_disabled_does_not_admit_parity_routes()
    {
        using var fixture = new DesktopParityFixture(desktopEnabled: false);
        using var catalog = await fixture.Send("GET", "/catalog/products");
        using var security = await fixture.Send("GET", "/security/users");
        Assert.Equal(HttpStatusCode.NotFound, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, security.StatusCode);
        Assert.Empty(fixture.State.Calls);
    }

    [Fact]
    public void Elevated_permission_guard_extends_only_exact_catalog_and_security_object_rights()
    {
        var method = typeof(MyDmsVn.Server.Infrastructure.Operations.SqlWebRuntimePrincipalValidator)
            .GetMethod("EffectiveElevatedPermissionSql", System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        var original = Assert.IsType<string>(method!.Invoke(null, new object[] { false, false }));
        var desktop = Assert.IsType<string>(method.Invoke(null, new object[] { false, true }));
        Assert.DoesNotContain("N'INSERT' AND candidate.name IN (N'Products'", original);
        Assert.Contains("N'INSERT' AND candidate.name IN (N'Products'", desktop);
        Assert.Contains("N'UPDATE' AND candidate.name IN (N'Products'", desktop);
        Assert.Contains("ownerTable.name IN (N'Products'", desktop);
        Assert.Contains("o.name IN (N'ApplySecurityAdministration',N'ReadSecurityPermissionImportTarget',N'ApplySecurityPermissionImport')", desktop);
        Assert.Contains("p.permission_name=N'REFERENCES'", desktop);
        Assert.Contains("N'ALTER', N'CONTROL', N'TAKE OWNERSHIP'", desktop);
    }

    [Fact]
    public async Task Server_error_mapping_preserves_concurrency_conflict()
    {
        using var fixture = new DesktopParityFixture();
        fixture.State.Failure = Error.Conflict("Security.RevisionConflict", "The security assignment changed.");
        using var response = await fixture.Send("POST", "/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":1}");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Security.RevisionConflict", (string?)JObject.Parse(await response.Content.ReadAsStringAsync())["error"]!["code"]);
        Assert.Single(fixture.State.Calls);
    }

    private static async Task Invalid(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ValidationError", (string?)JObject.Parse(await response.Content.ReadAsStringAsync())["error"]!["code"]);
    }
}

internal sealed class DesktopParityFixture : IDisposable
{
    public readonly DesktopParityState State = new DesktopParityState();
    private readonly AuthFixture _host;
    private string? _token;
    public AuthState Store => _host.Store;
    public DesktopParityFixture(bool desktopEnabled = true)
    {
        var options = new WebHostOptions(AuthFixture.Origin, AuthFixture.Origin, "auth-tests", "active",
            new Dictionary<string, byte[]> { ["active"] = Encoding.UTF8.GetBytes("active-test-key-at-least-32-bytes-long"),
                ["previous"] = Encoding.UTF8.GetBytes("previous-test-key-at-least-32-bytes-long") },
            desktopApiEnabled: desktopEnabled);
        _host = new AuthFixture(services =>
        {
            services.AddSingleton<IPermissionStore>(State);
            Add<ListProductsQuery, PagedResult<ProductDto>>(services, r => new PagedResult<ProductDto>(new[] { new ProductDto(1, "P1", "Product", "kg", true) }, r.Request.PageNumber, r.Request.PageSize, 1));
            Add<GetProductByIdQuery, ProductDto>(services, _ => new ProductDto(1, "P1", "Product", "kg", true));
            Add<CreateProductCommand, ProductDto>(services, _ => new ProductDto(1, "P1", "Product", "kg", true));
            Add<UpdateProductCommand, ProductDto>(services, _ => new ProductDto(1, "P1", "Product", "kg", true));
            Add<SetProductActiveCommand, UnitResponse>(services, _ => UnitResponse.Value);
            Add<ListWarehousesQuery, PagedResult<WarehouseDto>>(services, r => new PagedResult<WarehouseDto>(new[] { new WarehouseDto(1, "W1", "Warehouse", null, true) }, r.Request.PageNumber, r.Request.PageSize, 1));
            Add<GetWarehouseByIdQuery, WarehouseDto>(services, _ => new WarehouseDto(1, "W1", "Warehouse", null, true));
            Add<CreateWarehouseCommand, WarehouseDto>(services, _ => new WarehouseDto(1, "W1", "Warehouse", null, true));
            Add<UpdateWarehouseCommand, WarehouseDto>(services, _ => new WarehouseDto(1, "W1", "Warehouse", null, true));
            Add<SetWarehouseActiveCommand, UnitResponse>(services, _ => UnitResponse.Value);
            Add<ListEmployeesQuery, PagedResult<EmployeeDto>>(services, r => new PagedResult<EmployeeDto>(new[] { new EmployeeDto(1, "E1", "Employee", null, 1, true) }, r.Request.PageNumber, r.Request.PageSize, 1));
            Add<GetEmployeeByIdQuery, EmployeeDto>(services, _ => new EmployeeDto(1, "E1", "Employee", null, 1, true));
            Add<CreateEmployeeCommand, EmployeeDto>(services, _ => new EmployeeDto(1, "E1", "Employee", null, 1, true));
            Add<UpdateEmployeeCommand, EmployeeDto>(services, _ => new EmployeeDto(1, "E1", "Employee", null, 1, true));
            Add<SetEmployeeActiveCommand, UnitResponse>(services, _ => UnitResponse.Value);
            Add<ListCustomersQuery, PagedResult<CustomerDto>>(services, r => new PagedResult<CustomerDto>(new[] { new CustomerDto(1, "C1", "Customer", null, null, null, true) }, r.Request.PageNumber, r.Request.PageSize, 1));
            Add<GetCustomerByIdQuery, CustomerDto>(services, _ => new CustomerDto(1, "C1", "Customer", null, null, null, true));
            Add<CreateCustomerCommand, CustomerDto>(services, _ => new CustomerDto(1, "C1", "Customer", null, null, null, true));
            Add<UpdateCustomerCommand, CustomerDto>(services, _ => new CustomerDto(1, "C1", "Customer", null, null, null, true));
            Add<SetCustomerActiveCommand, UnitResponse>(services, _ => UnitResponse.Value);
            Add<CheckPermissionQuery, PermissionDecisionDto>(services, r =>
                new PermissionDecisionDto(r.PermissionKey, !State.Denied.Contains(r.PermissionKey)));
            Add<ListPermissionCatalogQuery, PermissionCatalogDto>(services, _ => new PermissionCatalogDto("1", "digest", new[]
                { new PermissionCatalogItemDto("Security.Users.Manage", "Bảo mật", "Quản lý \"người dùng\"", "Active") }));
            Add<ListSecurityUsersQuery, IReadOnlyList<SecurityUserDto>>(services, _ => new[]
                { new SecurityUserDto(1, "alice", "Álice \"Admin\"", null, true, 9007199254740993L,
                    new[] { 7 }, new[] { new SecurityDecisionDto("Security.Users.Manage", null) }, true) });
            Add<ListSecurityRolesQuery, IReadOnlyList<SecurityRoleDto>>(services, _ => new[]
                { new SecurityRoleDto(7, "Quản trị", true, 9007199254740993L, new[] { 1 }, new[] { "Security.Users.Manage" }) });
            Add<MutateSecurityAdministrationCommand, SecurityAdministrationResultDto>(services, _ => new SecurityAdministrationResultDto(1, true));
            Add<PreviewPermissionAssignmentImportQuery, PermissionAssignmentImportPreviewDto>(services, _ =>
                new PermissionAssignmentImportPreviewDto(new ParsedPermissionAssignmentImportDto(1, PermissionImportTargetTypeDto.Role, 3,
                    new[] { new PermissionAssignmentImportEntryDto("Báo cáo \"Đặc biệt\"\n", true) }), "Quản trị", 1, "digest",
                    new[] { new PermissionAssignmentImportEntryDto("Báo cáo \"Đặc biệt\"\n", true) }));
            Add<ApplyPermissionAssignmentImportCommand, PermissionAssignmentImportResultDto>(services, _ => new PermissionAssignmentImportResultDto(9007199254740993L, 1, true));
            Add<GetFoundationStatusQuery, FoundationStatus>(services, _ => new FoundationStatus(true, "net48"));
        }, options: options);
    }
    private void Add<TReq, TResult>(IServiceCollection services, Func<TReq, TResult> result)
        where TReq : IRequest<ErrorOr<TResult>> =>
        services.AddSingleton<IRequestHandler<TReq, ErrorOr<TResult>>>(new CaptureHandler<TReq, TResult>(State, request =>
        {
            State.Calls.Add(request!);
            if (State.Failure.HasValue) return State.Failure.Value;
            return result(request);
        }));
    public async Task<HttpResponseMessage> Send(string method, string route, string? body = null,
        bool authenticated = true, string? correlationId = null)
    {
        if (authenticated && _token == null)
        {
            using var login = await _host.Login();
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            _token = (string)JObject.Parse(await login.Content.ReadAsStringAsync())["data"]!["accessToken"]!;
        }
        using var request = new HttpRequestMessage(new HttpMethod(method), AuthFixture.Origin + "/api/v1" + route);
        if (authenticated) request.Headers.Add("Authorization", "Bearer " + _token);
        if (correlationId != null) request.Headers.Add("X-Correlation-ID", correlationId);
        if (method != "GET")
        {
            request.Headers.Add("Origin", AuthFixture.Origin);
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
        }
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        return await _host.Server.HttpClient.SendAsync(request);
    }
    public void Dispose() => _host.Dispose();
    private sealed class CaptureHandler<TReq, TResult> : IRequestHandler<TReq, ErrorOr<TResult>>
        where TReq : IRequest<ErrorOr<TResult>>
    {
        private readonly Func<TReq, ErrorOr<TResult>> _handle;
        private readonly DesktopParityState _state;
        public CaptureHandler(DesktopParityState state, Func<TReq, ErrorOr<TResult>> handle)
        { _state = state; _handle = handle; }
        public Task<ErrorOr<TResult>> Handle(TReq request, CancellationToken token) =>
            _state.ThrowHandlers
                ? throw new InvalidOperationException("secret-database-connection")
                : Task.FromResult(_handle(request));
    }
}
internal sealed class DesktopParityState : IPermissionStore
{
    public readonly List<object> Calls = new List<object>();
    public readonly HashSet<string> Denied = new HashSet<string>(StringComparer.Ordinal);
    public Error? Failure;
    public bool ThrowHandlers;
    public Task<PermissionSnapshot> GetSnapshotAsync(int userId, string permissionKey, CancellationToken token) =>
        Task.FromResult(new PermissionSnapshot(!Denied.Contains(permissionKey), false));
}
