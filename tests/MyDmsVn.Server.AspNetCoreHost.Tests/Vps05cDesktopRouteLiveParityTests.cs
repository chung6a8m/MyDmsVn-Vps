using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Catalog;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.Application.Security;
using MyDmsVn.Server.HttpHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

/// <summary>
/// Live native-router/controller differential for the 28 VPS-02 signatures that
/// were ROUTE_ONLY before VPS-05C. Expected bodies are produced by the isolated
/// legacy net48 Json.NET settings; the request is admitted by the native JWT and
/// session boundary before ASP.NET Core dispatches the real controller action.
/// </summary>
public sealed class Vps05cDesktopRouteLiveParityTests
{
    private const string Correlation = "vps05c_live_28";

    public static TheoryData<string, string, string, string?, string> Cases => new()
    {
        { "VPS02-PRODUCT-LIST", "GET", "/api/v1/catalog/products?search=abc&pageNumber=2&pageSize=25&includeInactive=true", null, "product-list" },
        { "VPS02-PRODUCT-GET", "GET", "/api/v1/catalog/products/1", null, "product" },
        { "VPS02-PRODUCT-CREATE", "POST", "/api/v1/catalog/products", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":\"kg\"}", "product" },
        { "VPS02-PRODUCT-UPDATE", "PUT", "/api/v1/catalog/products/1", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":\"kg\"}", "product" },
        { "VPS02-PRODUCT-ACTIVE", "PUT", "/api/v1/catalog/products/1/active", "{\"isActive\":false}", "unit" },
        { "VPS02-WAREHOUSE-LIST", "GET", "/api/v1/catalog/warehouses?search=abc&pageNumber=2&pageSize=25&includeInactive=true", null, "warehouse-list" },
        { "VPS02-WAREHOUSE-GET", "GET", "/api/v1/catalog/warehouses/1", null, "warehouse" },
        { "VPS02-WAREHOUSE-CREATE", "POST", "/api/v1/catalog/warehouses", "{\"code\":\"W1\",\"name\":\"Warehouse\",\"address\":null}", "warehouse" },
        { "VPS02-WAREHOUSE-UPDATE", "PUT", "/api/v1/catalog/warehouses/1", "{\"code\":\"W1\",\"name\":\"Warehouse\",\"address\":null}", "warehouse" },
        { "VPS02-WAREHOUSE-ACTIVE", "PUT", "/api/v1/catalog/warehouses/1/active", "{\"isActive\":false}", "unit" },
        { "VPS02-EMPLOYEE-LIST", "GET", "/api/v1/catalog/employees?search=abc&pageNumber=2&pageSize=25&includeInactive=true", null, "employee-list" },
        { "VPS02-EMPLOYEE-GET", "GET", "/api/v1/catalog/employees/1", null, "employee" },
        { "VPS02-EMPLOYEE-CREATE", "POST", "/api/v1/catalog/employees", "{\"code\":\"E1\",\"name\":\"Employee\",\"phone\":null,\"userId\":1}", "employee" },
        { "VPS02-EMPLOYEE-UPDATE", "PUT", "/api/v1/catalog/employees/1", "{\"code\":\"E1\",\"name\":\"Employee\",\"phone\":null,\"userId\":1}", "employee" },
        { "VPS02-EMPLOYEE-ACTIVE", "PUT", "/api/v1/catalog/employees/1/active", "{\"isActive\":false}", "unit" },
        { "VPS02-CUSTOMER-LIST", "GET", "/api/v1/catalog/customers?search=abc&pageNumber=2&pageSize=25&includeInactive=true", null, "customer-list" },
        { "VPS02-CUSTOMER-GET", "GET", "/api/v1/catalog/customers/1", null, "customer" },
        { "VPS02-CUSTOMER-CREATE", "POST", "/api/v1/catalog/customers", "{\"code\":\"C1\",\"name\":\"Customer\",\"address\":null,\"phone\":null,\"taxCode\":null}", "customer" },
        { "VPS02-CUSTOMER-UPDATE", "PUT", "/api/v1/catalog/customers/1", "{\"code\":\"C1\",\"name\":\"Customer\",\"address\":null,\"phone\":null,\"taxCode\":null}", "customer" },
        { "VPS02-CUSTOMER-ACTIVE", "PUT", "/api/v1/catalog/customers/1/active", "{\"isActive\":false}", "unit" },
        { "VPS02-PERMISSION-CHECK", "GET", "/api/v1/security/permissions/check?key=Catalog.Products.Read", null, "permission-check" },
        { "VPS02-PERMISSION-CATALOG", "GET", "/api/v1/security/permissions/catalog", null, "permission-catalog" },
        { "VPS02-USERS", "GET", "/api/v1/security/users", null, "users" },
        { "VPS02-ROLES", "GET", "/api/v1/security/roles", null, "roles" },
        { "VPS02-SECURITY-MUTATION", "POST", "/api/v1/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993}", "mutation" },
        { "VPS02-IMPORT-PREVIEW", "POST", "/api/v1/security/import/preview", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\"}", "import-preview" },
        { "VPS02-IMPORT-APPLY", "POST", "/api/v1/security/import/apply", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"expectedTargetRevision\":9007199254740993,\"expectedCatalogDigest\":\"digest\"}", "import-apply" },
        { "VPS02-FOUNDATION", "GET", "/api/v1/foundation/status?clientName=Desktop", null, "foundation" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PreviouslyRouteOnlySignatureDispatchesAndMatchesLegacyJsonNet(
        string caseId, string method, string path, string? body, string expectedKind)
    {
        await using var fixture = await DesktopLiveFixture.StartAsync();
        using var response = await fixture.SendAsync(method, path, body);
        var actual = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            caseId + " expected 200, actual " + (int)response.StatusCode + ": " + actual +
            Environment.NewLine + fixture.LastException);
        Assert.Equal(Correlation, response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.True(response.Headers.CacheControl!.NoStore, caseId);
        Assert.Equal(LegacyExpected(expectedKind), actual);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PreviouslyRouteOnlySignaturePreservesRouteSpecificPermissionDecision(
        string caseId, string method, string path, string? body, string expectedKind)
    {
        _ = expectedKind;
        await using var fixture = await DesktopLiveFixture.StartAsync(denyPermissions: true);
        using var response = await fixture.SendAsync(method, path, body);
        var actual = await response.Content.ReadAsStringAsync();

        if (expectedKind is "permission-check" or "foundation")
        {
            // These authenticated introspection routes intentionally have no
            // separate object permission; their handlers report the decision.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(1, fixture.HandlerCalls);
            return;
        }

        Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
            caseId + " expected 403, actual " + (int)response.StatusCode + ": " + actual);
        AssertEnvelope(response, actual, "Auth.Forbidden");
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PreviouslyRouteOnlySignatureRejectsInvalidQueryOrBodyBeforeHandler(
        string caseId, string method, string path, string? body, string expectedKind)
    {
        _ = expectedKind;
        await using var fixture = await DesktopLiveFixture.StartAsync();
        var invalidPath = method == "GET" ? path + (path.Contains('?') ? "&" : "?") + "unknown=1" : path;
        using var response = await fixture.SendAsync(method, invalidPath, method == "GET" ? body : "{}");
        var actual = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
            caseId + " expected 400, actual " + (int)response.StatusCode + ": " + actual);
        AssertEnvelope(response, actual, "ValidationError");
        Assert.Equal(0, fixture.HandlerCalls);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task PreviouslyRouteOnlySignatureSanitizesPersistenceFailureAfterDispatch(
        string caseId, string method, string path, string? body, string expectedKind)
    {
        _ = expectedKind;
        await using var fixture = await DesktopLiveFixture.StartAsync(throwHandlers: true);
        using var response = await fixture.SendAsync(method, path, body);
        var actual = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.InternalServerError,
            caseId + " expected 500, actual " + (int)response.StatusCode + ": " + actual);
        AssertEnvelope(response, actual, "InternalError");
        Assert.DoesNotContain("secret-database", actual, StringComparison.Ordinal);
        Assert.Equal(1, fixture.HandlerCalls);
    }

    private static void AssertEnvelope(HttpResponseMessage response, string body, string code)
    {
        Assert.Equal(Correlation, response.Headers.GetValues("X-Correlation-ID").Single());
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(Correlation,
            json.RootElement.GetProperty("error").GetProperty("correlationId").GetString());
    }

    private static string LegacyExpected(string kind)
    {
        object value = kind switch
        {
            "product-list" => new PagedResult<ProductDto>(new[] { Product() }, 2, 25, 1),
            "product" => Product(),
            "warehouse-list" => new PagedResult<WarehouseDto>(new[] { Warehouse() }, 2, 25, 1),
            "warehouse" => Warehouse(),
            "employee-list" => new PagedResult<EmployeeDto>(new[] { Employee() }, 2, 25, 1),
            "employee" => Employee(),
            "customer-list" => new PagedResult<CustomerDto>(new[] { Customer() }, 2, 25, 1),
            "customer" => Customer(),
            "unit" => UnitResponse.Value,
            "permission-check" => new PermissionDecisionDto("Catalog.Products.Read", true),
            "permission-catalog" => new PermissionCatalogDto("1", "digest", Array.Empty<PermissionCatalogItemDto>()),
            "users" => Array.Empty<SecurityUserDto>(),
            "roles" => Array.Empty<SecurityRoleDto>(),
            "mutation" => new SecurityAdministrationResultDto(9007199254740993L, true),
            "import-preview" => ImportPreview(),
            "import-apply" => new PermissionAssignmentImportResultDto(9007199254740993L, 1, true),
            "foundation" => new FoundationStatus(true, "net48"),
            _ => throw new InvalidOperationException("Unknown expected kind: " + kind),
        };
        return JsonConvert.SerializeObject(new { data = value }, new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
        });
    }

    private static ProductDto Product() => new(1, "P1", "Product", "kg", true);
    private static WarehouseDto Warehouse() => new(1, "W1", "Warehouse", null, true);
    private static EmployeeDto Employee() => new(1, "E1", "Employee", null, 1, true);
    private static CustomerDto Customer() => new(1, "C1", "Customer", null, null, null, true);
    private static PermissionAssignmentImportPreviewDto ImportPreview() => new(
        new ParsedPermissionAssignmentImportDto(1, PermissionImportTargetTypeDto.Role, 3,
            Array.Empty<PermissionAssignmentImportEntryDto>()),
        "Role", 0, "digest", Array.Empty<PermissionAssignmentImportEntryDto>());

    private sealed class DesktopLiveFixture : IAsyncDisposable
    {
        private static readonly Guid SessionId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        private readonly WebApplication _app;
        private readonly HttpClient _client;
        private readonly string _token;
        private readonly ConcurrentQueue<Exception> _errors;
        private readonly DesktopState _state;

        private DesktopLiveFixture(WebApplication app, HttpClient client, string token,
            ConcurrentQueue<Exception> errors, DesktopState state)
        { _app = app; _client = client; _token = token; _errors = errors; _state = state; }

        public Exception? LastException => _errors.LastOrDefault();
        public int HandlerCalls => _state.Calls.Count;

        public static async Task<DesktopLiveFixture> StartAsync(
            bool denyPermissions = false, bool throwHandlers = false)
        {
            var clock = new FrozenClock();
            var options = new WebHostOptions("https://localhost", "https://localhost", "vps05c-live",
                "active", new Dictionary<string, byte[]>
                {
                    ["active"] = Encoding.UTF8.GetBytes("active-test-key-at-least-32-bytes-long")
                }, desktopApiEnabled: true);
            var state = new DesktopState
            {
                DenyPermissions = denyPermissions,
                ThrowHandlers = throwHandlers,
            };
            var errors = new ConcurrentQueue<Exception>();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddServerApplication().AddHttpRequestIdentity();
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton<IUtcClock>(clock);
            builder.Services.AddSingleton<AuthAttemptLimiter>();
            builder.Services.AddScoped<CoreApiAdmissionMiddleware>();
            builder.Services.AddSingleton<ISecurityAuditReadiness, CoreSecurityAuditReadiness>();
            builder.Services.AddSingleton<IWebAuthSessionStore>(state);
            builder.Services.AddSingleton<IPermissionStore>(state);
            AddHandlers(builder.Services, state);

            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                try { await next().ConfigureAwait(false); }
                catch (Exception error) { errors.Enqueue(error); throw; }
            });
            app.Use(async (context, next) =>
            {
                // Kestrel uses an ephemeral loopback HTTP listener only as an in-process
                // test transport. Admission still sees the production public HTTPS origin.
                context.Request.Scheme = "https";
                context.Request.Host = new Microsoft.AspNetCore.Http.HostString("localhost");
                await next().ConfigureAwait(false);
            });
            app.UseRouting();
            app.UseMiddleware<CoreApiAdmissionMiddleware>();
            Net10RouteTable.Map(app);
            Net10RouteTable.MapFallback(app);
            await app.StartAsync();

            var address = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            var user = CurrentUser.Authenticated(1, "alice", "Alice", true);
            var session = new WebAuthSessionToken(SessionId, user,
                "22222222-2222-4222-8222-222222222222", clock.UtcNow.AddDays(7), clock.UtcNow.AddDays(30));
            var token = new AccessTokenIssuer(options, clock).Issue(session).Value;
            return new DesktopLiveFixture(app, new HttpClient { BaseAddress = new Uri(address) }, token, errors, state);
        }

        public async Task<HttpResponseMessage> SendAsync(string method, string path, string? body)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
            request.Headers.Add("X-Correlation-ID", Correlation);
            if (method is "POST" or "PUT")
            {
                request.Headers.Add("Origin", "https://localhost");
                request.Headers.Add("Sec-Fetch-Site", "same-origin");
            }
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return await _client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static void AddHandlers(IServiceCollection services, DesktopState state)
        {
            Add<ListProductsQuery, PagedResult<ProductDto>>(services, state, r => new(new[] { Product() }, r.Request.PageNumber, r.Request.PageSize, 1));
            Add<GetProductByIdQuery, ProductDto>(services, state, _ => Product());
            Add<CreateProductCommand, ProductDto>(services, state, _ => Product());
            Add<UpdateProductCommand, ProductDto>(services, state, _ => Product());
            Add<SetProductActiveCommand, UnitResponse>(services, state, _ => UnitResponse.Value);
            Add<ListWarehousesQuery, PagedResult<WarehouseDto>>(services, state, r => new(new[] { Warehouse() }, r.Request.PageNumber, r.Request.PageSize, 1));
            Add<GetWarehouseByIdQuery, WarehouseDto>(services, state, _ => Warehouse());
            Add<CreateWarehouseCommand, WarehouseDto>(services, state, _ => Warehouse());
            Add<UpdateWarehouseCommand, WarehouseDto>(services, state, _ => Warehouse());
            Add<SetWarehouseActiveCommand, UnitResponse>(services, state, _ => UnitResponse.Value);
            Add<ListEmployeesQuery, PagedResult<EmployeeDto>>(services, state, r => new(new[] { Employee() }, r.Request.PageNumber, r.Request.PageSize, 1));
            Add<GetEmployeeByIdQuery, EmployeeDto>(services, state, _ => Employee());
            Add<CreateEmployeeCommand, EmployeeDto>(services, state, _ => Employee());
            Add<UpdateEmployeeCommand, EmployeeDto>(services, state, _ => Employee());
            Add<SetEmployeeActiveCommand, UnitResponse>(services, state, _ => UnitResponse.Value);
            Add<ListCustomersQuery, PagedResult<CustomerDto>>(services, state, r => new(new[] { Customer() }, r.Request.PageNumber, r.Request.PageSize, 1));
            Add<GetCustomerByIdQuery, CustomerDto>(services, state, _ => Customer());
            Add<CreateCustomerCommand, CustomerDto>(services, state, _ => Customer());
            Add<UpdateCustomerCommand, CustomerDto>(services, state, _ => Customer());
            Add<SetCustomerActiveCommand, UnitResponse>(services, state, _ => UnitResponse.Value);
            Add<CheckPermissionQuery, PermissionDecisionDto>(services, state, r => new(r.PermissionKey, true));
            Add<ListPermissionCatalogQuery, PermissionCatalogDto>(services, state, _ => new("1", "digest", Array.Empty<PermissionCatalogItemDto>()));
            Add<ListSecurityUsersQuery, IReadOnlyList<SecurityUserDto>>(services, state, _ => Array.Empty<SecurityUserDto>());
            Add<ListSecurityRolesQuery, IReadOnlyList<SecurityRoleDto>>(services, state, _ => Array.Empty<SecurityRoleDto>());
            Add<MutateSecurityAdministrationCommand, SecurityAdministrationResultDto>(services, state, _ => new(9007199254740993L, true));
            Add<PreviewPermissionAssignmentImportQuery, PermissionAssignmentImportPreviewDto>(services, state, _ => ImportPreview());
            Add<ApplyPermissionAssignmentImportCommand, PermissionAssignmentImportResultDto>(services, state, _ => new(9007199254740993L, 1, true));
            Add<GetFoundationStatusQuery, FoundationStatus>(services, state, _ => new(true, "net48"));
        }

        private static void Add<TRequest, TResult>(IServiceCollection services, DesktopState state,
            Func<TRequest, TResult> result) where TRequest : IRequest<ErrorOr<TResult>> =>
            services.AddSingleton<IRequestHandler<TRequest, ErrorOr<TResult>>>(
                new CaptureHandler<TRequest, TResult>(state, result));

        private sealed class CaptureHandler<TRequest, TResult> : IRequestHandler<TRequest, ErrorOr<TResult>>
            where TRequest : IRequest<ErrorOr<TResult>>
        {
            private readonly DesktopState _state;
            private readonly Func<TRequest, TResult> _result;
            public CaptureHandler(DesktopState state, Func<TRequest, TResult> result)
            { _state = state; _result = result; }
            public Task<ErrorOr<TResult>> Handle(TRequest request, CancellationToken cancellationToken)
            {
                _state.Calls.Add(request!);
                if (_state.ThrowHandlers)
                    throw new InvalidOperationException("secret-database-connection");
                return Task.FromResult<ErrorOr<TResult>>(_result(request));
            }
        }
    }

    private sealed class FrozenClock : IUtcClock
    {
        public DateTime UtcNow => new(2026, 11, 18, 1, 0, 0, DateTimeKind.Utc);
    }

    private sealed class DesktopState : IWebAuthSessionStore, IPermissionStore
    {
        public List<object> Calls { get; } = new();
        public bool DenyPermissions { get; init; }
        public bool ThrowHandlers { get; init; }
        public Task<CurrentUser?> GetActiveUserAsync(Guid sessionId, int userId, CancellationToken token) =>
            Task.FromResult<CurrentUser?>(sessionId == Guid.Parse("11111111-1111-4111-8111-111111111111") && userId == 1
                ? CurrentUser.Authenticated(1, "alice", "Alice", true) : null);
        public Task<WebAuthSessionToken?> IssueAsync(int userId, CancellationToken token) => throw new NotSupportedException();
        public Task<WebAuthSessionToken?> RotateAsync(string refreshToken, CancellationToken token) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken token) => throw new NotSupportedException();
        public Task<PermissionSnapshot> GetSnapshotAsync(int userId, string permissionKey, CancellationToken token) =>
            Task.FromResult(new PermissionSnapshot(!DenyPermissions, false));
    }
}
