using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MyDmsVn.Server.Application.Security;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Server.HttpHost.Tests;

public sealed class Vps05cDesktopRouteNet48OracleTests
{
    private const string Correlation = "vps05c_live_28";
    private sealed class Case
    {
        public Case(string id, string method, string path, string? body)
        {
            Id = id;
            Method = method;
            Path = path;
            Body = body;
        }

        public string Id { get; }
        public string Method { get; }
        public string Path { get; }
        public string? Body { get; }
    }

    private sealed class Observation
    {
        public Observation(string caseId, string variant, int status, string contentType,
            string correlationId, string cacheControl, string bodyBase64)
        {
            CaseId = caseId;
            Variant = variant;
            Status = status;
            ContentType = contentType;
            CorrelationId = correlationId;
            CacheControl = cacheControl;
            BodyBase64 = bodyBase64;
        }

        public string CaseId { get; }
        public string Variant { get; }
        public int Status { get; }
        public string ContentType { get; }
        public string CorrelationId { get; }
        public string CacheControl { get; }
        public string BodyBase64 { get; }
    }

    private static readonly IReadOnlyList<Case> Cases = new[]
    {
        new Case("VPS02-PRODUCT-LIST", "GET", "/catalog/products?search=abc&pageNumber=2&pageSize=25&includeInactive=true", null),
        new Case("VPS02-PRODUCT-GET", "GET", "/catalog/products/1", null),
        new Case("VPS02-PRODUCT-CREATE", "POST", "/catalog/products", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":\"kg\"}"),
        new Case("VPS02-PRODUCT-UPDATE", "PUT", "/catalog/products/1", "{\"code\":\"P1\",\"name\":\"Product\",\"unit\":\"kg\"}"),
        new Case("VPS02-PRODUCT-ACTIVE", "PUT", "/catalog/products/1/active", "{\"isActive\":false}"),
        new Case("VPS02-WAREHOUSE-LIST", "GET", "/catalog/warehouses?search=abc&pageNumber=2&pageSize=25&includeInactive=true", null),
        new Case("VPS02-WAREHOUSE-GET", "GET", "/catalog/warehouses/1", null),
        new Case("VPS02-WAREHOUSE-CREATE", "POST", "/catalog/warehouses", "{\"code\":\"W1\",\"name\":\"Warehouse\",\"address\":null}"),
        new Case("VPS02-WAREHOUSE-UPDATE", "PUT", "/catalog/warehouses/1", "{\"code\":\"W1\",\"name\":\"Warehouse\",\"address\":null}"),
        new Case("VPS02-WAREHOUSE-ACTIVE", "PUT", "/catalog/warehouses/1/active", "{\"isActive\":false}"),
        new Case("VPS02-EMPLOYEE-LIST", "GET", "/catalog/employees?search=abc&pageNumber=2&pageSize=25&includeInactive=true", null),
        new Case("VPS02-EMPLOYEE-GET", "GET", "/catalog/employees/1", null),
        new Case("VPS02-EMPLOYEE-CREATE", "POST", "/catalog/employees", "{\"code\":\"E1\",\"name\":\"Employee\",\"phone\":null,\"userId\":1}"),
        new Case("VPS02-EMPLOYEE-UPDATE", "PUT", "/catalog/employees/1", "{\"code\":\"E1\",\"name\":\"Employee\",\"phone\":null,\"userId\":1}"),
        new Case("VPS02-EMPLOYEE-ACTIVE", "PUT", "/catalog/employees/1/active", "{\"isActive\":false}"),
        new Case("VPS02-CUSTOMER-LIST", "GET", "/catalog/customers?search=abc&pageNumber=2&pageSize=25&includeInactive=true", null),
        new Case("VPS02-CUSTOMER-GET", "GET", "/catalog/customers/1", null),
        new Case("VPS02-CUSTOMER-CREATE", "POST", "/catalog/customers", "{\"code\":\"C1\",\"name\":\"Customer\",\"address\":null,\"phone\":null,\"taxCode\":null}"),
        new Case("VPS02-CUSTOMER-UPDATE", "PUT", "/catalog/customers/1", "{\"code\":\"C1\",\"name\":\"Customer\",\"address\":null,\"phone\":null,\"taxCode\":null}"),
        new Case("VPS02-CUSTOMER-ACTIVE", "PUT", "/catalog/customers/1/active", "{\"isActive\":false}"),
        new Case("VPS02-PERMISSION-CHECK", "GET", "/security/permissions/check?key=Catalog.Products.Read", null),
        new Case("VPS02-PERMISSION-CATALOG", "GET", "/security/permissions/catalog", null),
        new Case("VPS02-USERS", "GET", "/security/users", null),
        new Case("VPS02-ROLES", "GET", "/security/roles", null),
        new Case("VPS02-SECURITY-MUTATION", "POST", "/security/mutations", "{\"target\":1,\"operation\":2,\"targetId\":1,\"isActive\":false,\"expectedRevision\":9007199254740993}"),
        new Case("VPS02-IMPORT-PREVIEW", "POST", "/security/import/preview", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\"}"),
        new Case("VPS02-IMPORT-APPLY", "POST", "/security/import/apply", "{\"targetType\":1,\"targetId\":3,\"json\":\"{\\\"schemaVersion\\\":1,\\\"targetType\\\":\\\"Role\\\",\\\"targetId\\\":3,\\\"entries\\\":[]}\",\"expectedTargetRevision\":9007199254740993,\"expectedCatalogDigest\":\"digest\"}"),
        new Case("VPS02-FOUNDATION", "GET", "/foundation/status?clientName=Desktop", null),
    };

    [Fact]
    public async Task LiveNet48OwinResponsesMatchFrozenVps05cOracle()
    {
        var observations = new List<Observation>();
        foreach (var testCase in Cases)
        {
            observations.Add(await Capture(testCase, "success"));
            observations.Add(await Capture(testCase, "permission"));
            observations.Add(await Capture(testCase, "invalid"));
            observations.Add(await Capture(testCase, "failure"));
        }

        var actual = JsonConvert.SerializeObject(observations, Formatting.Indented);
        if (Environment.GetEnvironmentVariable("MYDMSVN_UPDATE_VPS05C_ORACLE") == "approved")
        {
            var output = Environment.GetEnvironmentVariable("MYDMSVN_VPS05C_ORACLE_OUTPUT");
            Assert.False(string.IsNullOrWhiteSpace(output));
            File.WriteAllText(output!, actual, new UTF8Encoding(false));
            return;
        }

        var expectedPath = Path.Combine(LocateRoot(), "docs", "contracts", "vps-05c-desktop-route-net48.json");
        Assert.True(File.Exists(expectedPath), "Frozen net48 route oracle is missing.");
        Assert.True(JToken.DeepEquals(JToken.Parse(File.ReadAllText(expectedPath)), JToken.Parse(actual)),
            "Live net48 OWIN route observations differ from the frozen VPS-05C oracle.");
    }

    private static async Task<Observation> Capture(Case testCase, string variant)
    {
        using var fixture = new DesktopParityFixture();
        var path = testCase.Path;
        var body = testCase.Body;
        if (variant == "permission")
        {
            var permission = Permission(testCase.Id);
            if (permission != null) fixture.State.Denied.Add(permission);
        }
        else if (variant == "invalid")
        {
            if (testCase.Method == "GET") path += path.Contains("?") ? "&unknown=1" : "?unknown=1";
            else body = "{}";
        }
        else if (variant == "failure") fixture.State.ThrowHandlers = true;

        using var response = await fixture.Send(testCase.Method, path, body, correlationId: Correlation);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        return new Observation(testCase.Id, variant, (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString() ?? string.Empty,
            response.Headers.TryGetValues("X-Correlation-ID", out var correlations) ? correlations.Single() : string.Empty,
            response.Headers.TryGetValues("Cache-Control", out var cache) ? string.Join(",", cache) : string.Empty,
            Convert.ToBase64String(bytes));
    }

    private static string? Permission(string caseId)
    {
        if (caseId == "VPS02-PERMISSION-CHECK") return PermissionKeys.CatalogProductsRead;
        if (caseId == "VPS02-FOUNDATION") return null;
        if (caseId.StartsWith("VPS02-PRODUCT-", StringComparison.Ordinal))
            return caseId.EndsWith("LIST", StringComparison.Ordinal) || caseId.EndsWith("GET", StringComparison.Ordinal)
                ? PermissionKeys.CatalogProductsRead : PermissionKeys.CatalogProductsWrite;
        if (caseId.StartsWith("VPS02-WAREHOUSE-", StringComparison.Ordinal))
            return caseId.EndsWith("LIST", StringComparison.Ordinal) || caseId.EndsWith("GET", StringComparison.Ordinal)
                ? PermissionKeys.CatalogWarehousesRead : PermissionKeys.CatalogWarehousesWrite;
        if (caseId.StartsWith("VPS02-EMPLOYEE-", StringComparison.Ordinal))
            return caseId.EndsWith("LIST", StringComparison.Ordinal) || caseId.EndsWith("GET", StringComparison.Ordinal)
                ? PermissionKeys.CatalogEmployeesRead : PermissionKeys.CatalogEmployeesWrite;
        if (caseId.StartsWith("VPS02-CUSTOMER-", StringComparison.Ordinal))
            return caseId.EndsWith("LIST", StringComparison.Ordinal) || caseId.EndsWith("GET", StringComparison.Ordinal)
                ? PermissionKeys.CatalogCustomersRead : PermissionKeys.CatalogCustomersWrite;
        if (caseId == "VPS02-PERMISSION-CATALOG") return PermissionKeys.SecurityPermissionsRead;
        if (caseId == "VPS02-USERS") return PermissionKeys.SecurityUsersManage;
        return PermissionKeys.SecurityRolesManage;
    }

    private static string LocateRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "docs", "contracts"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
