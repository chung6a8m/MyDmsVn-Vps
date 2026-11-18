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
    private class Case
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

    private sealed class LegacyCase : Case
    {
        public LegacyCase(string id, string method, string path, string? requestFile, string responseFile)
            : base(id, method, path, requestFile)
        { ResponseFile = responseFile; }
        public string ResponseFile { get; }
    }

    private sealed class Observation
    {
        public Observation(string caseId, string variant, int status, string contentType,
            string correlationId, string cacheControl, string pragma, string wwwAuthenticate,
            string xContentTypeOptions, string hsts, string setCookie, string bodyBase64)
        {
            CaseId = caseId;
            Variant = variant;
            Status = status;
            ContentType = contentType;
            CorrelationId = correlationId;
            CacheControl = cacheControl;
            Pragma = pragma;
            WwwAuthenticate = wwwAuthenticate;
            XContentTypeOptions = xContentTypeOptions;
            Hsts = hsts;
            SetCookie = setCookie;
            BodyBase64 = bodyBase64;
        }

        public string CaseId { get; }
        public string Variant { get; }
        public int Status { get; }
        public string ContentType { get; }
        public string CorrelationId { get; }
        public string CacheControl { get; }
        public string Pragma { get; }
        public string WwwAuthenticate { get; }
        public string XContentTypeOptions { get; }
        public string Hsts { get; }
        public string SetCookie { get; }
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

    private static readonly IReadOnlyList<Case> ControlCases = new[]
    {
        new Case("VPS01-HEALTH", "GET", "/api/v1/health", null),
        new Case("VPS01-AUTH-LOGIN", "POST", "/api/v1/auth/login", "{\"username\":\"alice\",\"password\":\"correct-secret\"}"),
        new Case("VPS01-AUTH-REFRESH", "POST", "/api/v1/auth/refresh", null),
        new Case("VPS01-AUTH-LOGOUT", "POST", "/api/v1/auth/logout", null),
        new Case("VPS01-AUTH-ME", "GET", "/api/v1/auth/me", null),
        new Case("VPS01-AUTH-CAPABILITIES", "GET", "/api/v1/auth/capabilities?feature=sales", null),
        new Case("VPS01-OPERATIONS-CONTEXT", "GET", "/api/v1/operations/context", null),
        new Case("VPS01-OPERATIONS-STATUS", "GET", "/api/v1/operations/11111111-1111-4111-8111-111111111111", null),
        new Case("VPS01-RESOLUTION-CONTEXT", "GET", "/api/v2/operations/context", null),
        new Case("VPS01-RESOLUTION-STATUS", "GET", "/api/v2/operations/11111111-1111-4111-8111-111111111111/resolution", null),
        new Case("VPS01-RESEND-CONTEXT", "GET", "/api/v3/operations/resend-context", null),
        new Case("VPS01-RESEND-RECEIPT", "GET", "/api/v3/operations/11111111-1111-4111-8111-111111111111/resend-receipt", null),
        new Case("VPS01-RESEND-ELIGIBILITY", "GET", "/api/v3/operations/11111111-1111-4111-8111-111111111111/resend-eligibility", null),
    };

    private static readonly IReadOnlyList<LegacyCase> LegacyCases = new[]
    {
        new LegacyCase("VPS01-SALES-LIST", "GET", "/sales-receipts", null, "sales-list-response.json"),
        new LegacyCase("VPS01-SALES-GET", "GET", "/sales-receipts/9223372036854775807", null, "sales-get-posted-response.json"),
        new LegacyCase("VPS01-SALES-CREATE", "POST", "/sales-receipts", "sales-create-request.json", "sales-create-response.json"),
        new LegacyCase("VPS01-SALES-UPDATE", "PUT", "/sales-receipts/9223372036854775807/draft", "sales-update-request.json", "sales-update-response.json"),
        new LegacyCase("VPS01-SALES-POST", "POST", "/sales-receipts/9223372036854775807/post", "sales-post-request.json", "sales-post-response.json"),
        new LegacyCase("VPS01-LOOKUP-PRODUCTS", "GET", "/catalog/products/lookup?search=abc&limit=25", null, "catalog-products-response.json"),
        new LegacyCase("VPS01-LOOKUP-CUSTOMERS", "GET", "/catalog/customers/lookup?search=abc&limit=25", null, "catalog-customers-response.json"),
        new LegacyCase("VPS01-LOOKUP-WAREHOUSES", "GET", "/catalog/warehouses/lookup?search=abc&limit=25", null, "catalog-warehouses-response.json"),
        new LegacyCase("VPS01-LOOKUP-EMPLOYEES", "GET", "/catalog/employees/lookup?search=abc&limit=25", null, "catalog-employees-response.json"),
        new LegacyCase("VPS01-RECEIPT-LIST", "GET", "/goods-receipts?pageNumber=1&pageSize=25", null, "receipt-list-response.json"),
        new LegacyCase("VPS01-RECEIPT-GET", "GET", "/goods-receipts/9007199254740993", null, "receipt-get-posted-response.json"),
        new LegacyCase("VPS01-RECEIPT-CREATE", "POST", "/goods-receipts", "receipt-create-request.json", "receipt-create-response.json"),
        new LegacyCase("VPS01-RECEIPT-UPDATE", "PUT", "/goods-receipts/9007199254740993/draft", "receipt-update-request.json", "receipt-update-response.json"),
        new LegacyCase("VPS01-RECEIPT-POST", "POST", "/goods-receipts/9007199254740993/post", "receipt-post-request.json", "receipt-post-response.json"),
        new LegacyCase("VPS01-INVENTORY-BALANCES", "GET", "/inventory/balances?pageNumber=1&pageSize=25", null, "balances-response.json"),
        new LegacyCase("VPS01-INVENTORY-STOCK-CARD", "GET", "/inventory/stock-card?productId=104&warehouseId=102&pageNumber=1&pageSize=25", null, "stock-card-response.json"),
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
            observations.Add(await Capture(testCase, "unauthenticated"));
        }
        foreach (var testCase in ControlCases)
            observations.Add(await CaptureControl(testCase,
                testCase.Id.Contains("OPERATIONS-") || testCase.Id.Contains("RESOLUTION-") || testCase.Id.Contains("RESEND-")
                    ? "disabled" : "success"));
        observations.Add(await CaptureControl(ControlCases.Single(x => x.Id == "VPS01-AUTH-LOGIN"), "invalid"));
        observations.Add(await CaptureControl(ControlCases.Single(x => x.Id == "VPS01-AUTH-LOGIN"), "failure"));
        observations.Add(await CaptureControl(ControlCases.Single(x => x.Id == "VPS01-AUTH-REFRESH"), "unauthenticated"));
        observations.Add(await CaptureControl(ControlCases.Single(x => x.Id == "VPS01-AUTH-REFRESH"), "failure"));
        observations.Add(await CaptureControl(ControlCases.Single(x => x.Id == "VPS01-AUTH-LOGOUT"), "invalid"));
        observations.Add(await CaptureControl(ControlCases.Single(x => x.Id == "VPS01-AUTH-LOGOUT"), "failure"));
        foreach (var id in new[] { "VPS01-AUTH-ME", "VPS01-AUTH-CAPABILITIES" })
        foreach (var variant in new[] { "unauthenticated", "invalid", "failure" })
            observations.Add(await CaptureControl(ControlCases.Single(x => x.Id == id), variant));
        observations.AddRange(await CaptureEnabledWeb6());
        foreach (var testCase in LegacyCases)
        foreach (var variant in new[] { "success", "permission", "invalid", "failure", "unauthenticated" })
            observations.Add(await CaptureLegacyData(testCase, variant));

        Assert.Equal(248, observations.Count);
        Assert.Equal(57, observations.Select(x => x.CaseId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(248, observations.Select(x => x.CaseId + "\n" + x.Variant)
            .Distinct(StringComparer.Ordinal).Count());

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

    private static async Task<Observation> CaptureLegacyData(LegacyCase testCase, string variant)
    {
        var path = testCase.Path;
        var bodyFile = testCase.Body;
        if (variant == "invalid")
        {
            if (testCase.Method == "GET") path += path.Contains("?") ? "&unknown=1" : "?unknown=1";
            else bodyFile = null;
        }
        var inventory = testCase.Id.IndexOf("RECEIPT", StringComparison.Ordinal) >= 0 ||
            testCase.Id.IndexOf("INVENTORY", StringComparison.Ordinal) >= 0;
        if (inventory)
        {
            using var fixture = new InventoryEndpointFixture { ResultFile = testCase.ResponseFile };
            if (variant == "permission") fixture.Denied.Add(LegacyPermission(testCase.Id));
            if (variant == "failure") fixture.Throw = true;
            using var response = await fixture.Send(testCase.Method, path,
                variant == "invalid" && testCase.Method != "GET" ? "{}" :
                    bodyFile == null ? null : InventoryEndpointCorpus.Text(bodyFile),
                authenticated: variant != "unauthenticated", correlationId: Correlation);
            return await Observe(testCase.Id, variant, response);
        }

        using (var fixture = new SalesWireFixture())
        {
            if (testCase.ResponseFile.StartsWith("sales-", StringComparison.Ordinal) &&
                testCase.ResponseFile != "sales-list-response.json" && testCase.ResponseFile != "sales-post-response.json")
                fixture.State.Detail = WireCorpus.Dto<MyDmsVn.Contracts.SalesReceiptDto>(testCase.ResponseFile);
            if (testCase.Id.StartsWith("VPS01-LOOKUP-", StringComparison.Ordinal))
            {
                var catalog = testCase.Id.Substring("VPS01-LOOKUP-".Length).ToLowerInvariant();
                fixture.State.Lookup = WireCorpus.From<IReadOnlyList<MyDmsVn.Contracts.CatalogLookupDto>>(
                    WireCorpus.Json("catalog-" + catalog + "-response.json")["data"]!);
            }
            if (variant == "permission") fixture.State.Denied.Add(LegacyPermission(testCase.Id));
            if (variant == "failure") fixture.State.Throw = true;
            using var response = await fixture.Send(testCase.Method, path,
                variant == "invalid" && testCase.Method != "GET" ? "{}" :
                    bodyFile == null ? null : WireCorpus.Text(bodyFile),
                authenticated: variant != "unauthenticated", correlationId: Correlation);
            return await Observe(testCase.Id, variant, response);
        }
    }

    private static async Task<Observation> CaptureControl(Case testCase, string variant)
    {
        using var fixture = new AuthFixture();
        if (variant == "failure")
        {
            if (testCase.Id == "VPS01-AUTH-LOGIN") fixture.Store.FailIssue = true;
            else if (testCase.Id == "VPS01-AUTH-REFRESH") fixture.Store.FailRotate = true;
            else if (testCase.Id == "VPS01-AUTH-LOGOUT") fixture.Store.FailRevoke = true;
            else if (testCase.Id == "VPS01-AUTH-ME") fixture.Store.FailActive = true;
            else fixture.Store.FailPermissions = true;
        }
        string? token = null;
        if (testCase.Id is "VPS01-AUTH-ME" or "VPS01-AUTH-CAPABILITIES" ||
            testCase.Id.StartsWith("VPS01-OPERATIONS-", StringComparison.Ordinal) ||
            testCase.Id.StartsWith("VPS01-RESOLUTION-", StringComparison.Ordinal) ||
            testCase.Id.StartsWith("VPS01-RESEND-", StringComparison.Ordinal))
        {
            using var login = await fixture.Login();
            token = (string)JObject.Parse(await login.Content.ReadAsStringAsync())["data"]!["accessToken"]!;
        }

        var path = testCase.Path;
        var body = testCase.Body;
        var cookie = testCase.Id is "VPS01-AUTH-REFRESH" or "VPS01-AUTH-LOGOUT";
        if (variant == "invalid")
        {
            if (testCase.Id == "VPS01-AUTH-LOGIN") body = "{}";
            else if (testCase.Id == "VPS01-AUTH-CAPABILITIES") path = "/api/v1/auth/capabilities?feature=sales-receipts";
            else path += "?unknown=1";
        }
        if (variant == "unauthenticated") { token = null; cookie = false; }
        using var request = new HttpRequestMessage(new HttpMethod(testCase.Method), AuthFixture.Origin + path);
        request.Headers.Add("X-Correlation-ID", Correlation);
        if (token != null) request.Headers.Add("Authorization", "Bearer " + token);
        if (testCase.Method == "POST")
        {
            request.Headers.Add("Origin", AuthFixture.Origin);
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        }
        if (cookie)
            request.Headers.Add("Cookie", "__Host-mydmsvn-rt=" + AuthFixture.InitialRefresh);
        if (body != null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await fixture.Server.HttpClient.SendAsync(request);
        if ((testCase.Id is "VPS01-AUTH-LOGIN" or "VPS01-AUTH-REFRESH") && response.IsSuccessStatusCode)
        {
            var issued = (string)JObject.Parse(await response.Content.ReadAsStringAsync())["data"]!["accessToken"]!;
            using var me = await fixture.Get("me", issued);
            Assert.Equal(System.Net.HttpStatusCode.OK, me.StatusCode);
        }
        return await Observe(testCase.Id, variant, response);
    }

    private static async Task<IReadOnlyList<Observation>> CaptureEnabledWeb6()
    {
        using var probe = new Web6ResendEligibilityEndpointTests.Probe();
        var result = new List<Observation>();
        using (var context = await probe.Get("resend-context", request =>
            request.Headers.Add("X-Correlation-ID", Correlation)))
            result.Add(await Observe("VPS01-RESEND-CONTEXT", "enabled", context));
        using (var receipt = await probe.Get(Web6ResendEligibilityEndpointTests.Operation.ToString("D") +
            "/resend-receipt", request =>
            {
                Web6ResendEligibilityEndpointTests.Probe.IdentityHeaders(request);
                request.Headers.Add("X-Correlation-ID", Correlation);
            }))
            result.Add(await Observe("VPS01-RESEND-RECEIPT", "enabled", receipt));
        var compact = await probe.Receipt();
        using (var eligibility = await probe.Eligibility(compact, request =>
            request.Headers.Add("X-Correlation-ID", Correlation)))
            result.Add(await Observe("VPS01-RESEND-ELIGIBILITY", "enabled", eligibility));
        return result;
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

        using var response = await fixture.Send(testCase.Method, path, body,
            authenticated: variant != "unauthenticated", correlationId: Correlation);
        return await Observe(testCase.Id, variant, response);
    }

    private static async Task<Observation> Observe(string caseId, string variant, HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if ((caseId is "VPS01-AUTH-LOGIN" or "VPS01-AUTH-REFRESH") && response.IsSuccessStatusCode)
        {
            var json = JObject.Parse(Encoding.UTF8.GetString(bytes));
            json["data"]!["accessToken"] = "<dynamic-jwt>";
            bytes = Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        }
        return new Observation(caseId, variant, (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString() ?? string.Empty,
            response.Headers.TryGetValues("X-Correlation-ID", out var correlations) ? correlations.Single() : string.Empty,
            response.Headers.TryGetValues("Cache-Control", out var cache) ? string.Join(",", cache) : string.Empty,
            response.Headers.TryGetValues("Pragma", out var pragma) ? string.Join(",", pragma) : string.Empty,
            response.Headers.TryGetValues("WWW-Authenticate", out var authenticate) ? string.Join(",", authenticate) : string.Empty,
            response.Headers.TryGetValues("X-Content-Type-Options", out var contentOptions) ? string.Join(",", contentOptions) : string.Empty,
            response.Headers.TryGetValues("Strict-Transport-Security", out var hsts) ? string.Join(",", hsts) : string.Empty,
            response.Headers.TryGetValues("Set-Cookie", out var cookies) ? string.Join("\n", cookies) : string.Empty,
            Convert.ToBase64String(bytes));
    }

    private static string LegacyPermission(string caseId)
    {
        if (caseId == "VPS01-SALES-LIST" || caseId == "VPS01-SALES-GET") return "Sales.Receipts.Read";
        if (caseId == "VPS01-SALES-CREATE" || caseId == "VPS01-SALES-UPDATE") return "Sales.Receipts.Write";
        if (caseId == "VPS01-SALES-POST") return "Sales.Receipts.Post";
        if (caseId.StartsWith("VPS01-LOOKUP-", StringComparison.Ordinal))
        {
            var name = caseId.Substring("VPS01-LOOKUP-".Length).ToLowerInvariant();
            return "Catalog." + char.ToUpperInvariant(name[0]) + name.Substring(1) + ".Read";
        }
        if (caseId == "VPS01-RECEIPT-LIST" || caseId == "VPS01-RECEIPT-GET") return "Inventory.GoodsReceipts.Read";
        if (caseId == "VPS01-RECEIPT-CREATE" || caseId == "VPS01-RECEIPT-UPDATE") return "Inventory.GoodsReceipts.Write";
        if (caseId == "VPS01-RECEIPT-POST") return "Inventory.GoodsReceipts.Post";
        return caseId == "VPS01-INVENTORY-BALANCES" ? "Inventory.Balances.Read" : "Inventory.StockCard.Read";
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
