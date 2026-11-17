using System;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Desktop.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03DesktopApiCaseExecutor
{
    internal async Task ExecuteAsync(Vps03AcceptanceCase c, Vps03OwnedEnvironment env, CancellationToken token)
    {
        Assert.Equal("method:" + c.Interface + "." + c.Method, c.Executor);
        var s = Guid.NewGuid().ToString("N").Substring(0, 10);
        switch (c.Interface)
        {
            case "IProductApiClient": await Product(c.Method, env, s, token); break;
            case "IWarehouseApiClient": await Warehouse(c.Method, env, s, token); break;
            case "IEmployeeApiClient": await Employee(c.Method, env, s, token); break;
            case "ICustomerApiClient": await Customer(c.Method, env, s, token); break;
            case "ISalesReceiptApiClient": await Sales(c.Method, env, s, token); break;
            case "IGoodsReceiptApiClient": await Goods(c.Method, env, s, token); break;
            case "IInventoryApiClient": await Inventory(c.Method, env, s, token); break;
            case "IIdentityApiClient": await Identity(c.Method, env, token); break;
            case "IPermissionApiClient": Ok(await Get<IPermissionApiClient>(env).CheckAsync("Catalog.Products.Read", token)); break;
            case "IPermissionCatalogApiClient": Ok(await Get<IPermissionCatalogApiClient>(env).ForSession(Session(env).Version).GetCatalogAsync(token)); break;
            case "ISecurityAdministrationApiClient": await Security(c.Method, env, s, token); break;
            case "IFoundationApiClient": Ok(await Get<IFoundationApiClient>(env).GetStatusAsync(new FoundationStatusRequest("vps03-issue8"), token)); break;
            default: throw new InvalidOperationException("Unbound VPS-03 case " + c.CaseId);
        }
    }

    private static async Task Product(string method, Vps03OwnedEnvironment env, string s, CancellationToken t)
    {
        var api = Get<IProductApiClient>(env);
        if (method == "ListAsync") { Ok(await api.ListAsync(List(), t)); return; }
        if (method == "CreateAsync") { Ok(await api.CreateAsync(ProductRequest("P" + s), t)); return; }
        var row = Ok(await api.CreateAsync(ProductRequest("P" + s), t));
        switch (method)
        {
            case "GetAsync": Assert.Equal(row.Id, Ok(await api.GetAsync(row.Id, t)).Id); break;
            case "UpdateAsync": Assert.Equal("Updated " + s, Ok(await api.UpdateAsync(row.Id, new SaveProductRequest(row.Code, "Updated " + s, "EA"), t)).Name); break;
            case "SetActiveAsync": Ok(await api.SetActiveAsync(row.Id, false, t)); Assert.False(Ok(await api.GetAsync(row.Id, t)).IsActive); break;
            case "LookupAsync": Assert.Contains(Ok(await api.LookupAsync(new CatalogLookupRequest(row.Code, 10), t)), x => x.Id == row.Id); break;
            default: throw Unknown(method);
        }
    }

    private static async Task Warehouse(string method, Vps03OwnedEnvironment env, string s, CancellationToken t)
    {
        var api = Get<IWarehouseApiClient>(env);
        if (method == "ListAsync") { Ok(await api.ListAsync(List(), t)); return; }
        if (method == "CreateAsync") { Ok(await api.CreateAsync(WarehouseRequest("W" + s), t)); return; }
        var row = Ok(await api.CreateAsync(WarehouseRequest("W" + s), t));
        switch (method)
        {
            case "GetAsync": Assert.Equal(row.Id, Ok(await api.GetAsync(row.Id, t)).Id); break;
            case "UpdateAsync": Assert.Equal("Updated " + s, Ok(await api.UpdateAsync(row.Id, new SaveWarehouseRequest(row.Code, "Updated " + s, "Address"), t)).Name); break;
            case "SetActiveAsync": Ok(await api.SetActiveAsync(row.Id, false, t)); Assert.False(Ok(await api.GetAsync(row.Id, t)).IsActive); break;
            case "LookupAsync": Assert.Contains(Ok(await api.LookupAsync(new CatalogLookupRequest(row.Code, 10), t)), x => x.Id == row.Id); break;
            default: throw Unknown(method);
        }
    }

    private static async Task Employee(string method, Vps03OwnedEnvironment env, string s, CancellationToken t)
    {
        var api = Get<IEmployeeApiClient>(env);
        if (method == "ListAsync") { Ok(await api.ListAsync(List(), t)); return; }
        if (method == "CreateAsync") { Ok(await api.CreateAsync(EmployeeRequest("E" + s), t)); return; }
        var row = Ok(await api.CreateAsync(EmployeeRequest("E" + s), t));
        switch (method)
        {
            case "GetAsync": Assert.Equal(row.Id, Ok(await api.GetAsync(row.Id, t)).Id); break;
            case "UpdateAsync": Assert.Equal("Updated " + s, Ok(await api.UpdateAsync(row.Id, new SaveEmployeeRequest(row.Code, "Updated " + s, "0900000000", null), t)).Name); break;
            case "SetActiveAsync": Ok(await api.SetActiveAsync(row.Id, false, t)); Assert.False(Ok(await api.GetAsync(row.Id, t)).IsActive); break;
            case "LookupAsync": Assert.Contains(Ok(await api.LookupAsync(new CatalogLookupRequest(row.Code, 10), t)), x => x.Id == row.Id); break;
            default: throw Unknown(method);
        }
    }

    private static async Task Customer(string method, Vps03OwnedEnvironment env, string s, CancellationToken t)
    {
        var api = Get<ICustomerApiClient>(env);
        if (method == "ListAsync") { Ok(await api.ListAsync(List(), t)); return; }
        if (method == "CreateAsync") { Ok(await api.CreateAsync(CustomerRequest("C" + s), t)); return; }
        var row = Ok(await api.CreateAsync(CustomerRequest("C" + s), t));
        switch (method)
        {
            case "GetAsync": Assert.Equal(row.Id, Ok(await api.GetAsync(row.Id, t)).Id); break;
            case "UpdateAsync": Assert.Equal("Updated " + s, Ok(await api.UpdateAsync(row.Id, new SaveCustomerRequest(row.Code, "Updated " + s, "Address", "0900000000", null), t)).Name); break;
            case "SetActiveAsync": Ok(await api.SetActiveAsync(row.Id, false, t)); Assert.False(Ok(await api.GetAsync(row.Id, t)).IsActive); break;
            case "LookupAsync": Assert.Contains(Ok(await api.LookupAsync(new CatalogLookupRequest(row.Code, 10), t)), x => x.Id == row.Id); break;
            default: throw Unknown(method);
        }
    }

    private static async Task Sales(string method, Vps03OwnedEnvironment env, string s, CancellationToken t)
    {
        var d = await Seed(env, s, t);
        var api = Get<ISalesReceiptApiClient>(env).ForSession(Session(env).Version);
        if (method == "ListAsync") { Ok(await api.ListAsync(new SalesReceiptListRequest(null, null, null, null, null, null, null, null), t)); return; }
        var request = new SaveSalesReceiptRequest(DateTime.Today, d.CustomerId, d.WarehouseId, d.EmployeeId, "VPS03 " + s,
            new[] { new SaveSalesReceiptLineRequest(0, d.ProductId, 1m, 12m, false) });
        var draft = Ok(await api.CreateDraftAsync(request, t));
        switch (method)
        {
            case "CreateDraftAsync": Assert.Equal("Draft", draft.Status); break;
            case "GetAsync": Assert.Equal(draft.SaleId, Ok(await api.GetAsync(draft.SaleId, t)).SaleId); break;
            case "UpdateDraftAsync": Assert.Equal("updated " + s, Ok(await api.UpdateDraftAsync(new UpdateSalesReceiptDraftRequest(draft.SaleId, draft.Version, DateTime.Today, d.CustomerId, d.WarehouseId, d.EmployeeId, "updated " + s, request.Lines), t)).Note); break;
            case "PostAsync": await PostStock(env, d, t); Assert.Equal("Posted", Ok(await api.PostAsync(new PostSalesReceiptRequest(draft.SaleId, draft.Version), t)).Status); break;
            default: throw Unknown(method);
        }
    }

    private static async Task Goods(string method, Vps03OwnedEnvironment env, string s, CancellationToken t)
    {
        var d = await Seed(env, s, t); var api = Get<IGoodsReceiptApiClient>(env);
        if (method == "ListAsync") { Ok(await api.ListAsync(new GoodsReceiptListRequest(1, 20, null, null), t)); return; }
        var request = new SaveGoodsReceiptRequest(DateTime.Today, d.WarehouseId, d.EmployeeId, "VPS03 " + s,
            new[] { new SaveGoodsReceiptLineRequest(d.ProductId, 10m, 5m) });
        var draft = Ok(await api.CreateDraftAsync(request, t));
        switch (method)
        {
            case "CreateDraftAsync": Assert.Equal("Draft", draft.Status); break;
            case "GetAsync": Assert.Equal(draft.Id, Ok(await api.GetAsync(draft.Id, t)).Id); break;
            case "UpdateDraftAsync": Assert.Equal("updated " + s, Ok(await api.UpdateDraftAsync(new UpdateGoodsReceiptDraftRequest(draft.Id, draft.Version, DateTime.Today, d.WarehouseId, d.EmployeeId, "updated " + s, request.Lines), t)).Note); break;
            case "PostAsync": Assert.Equal("Posted", Ok(await api.PostAsync(new PostGoodsReceiptRequest(draft.Id, draft.Version), t)).Status); break;
            default: throw Unknown(method);
        }
    }

    private static async Task Inventory(string method, Vps03OwnedEnvironment env, string s, CancellationToken t)
    {
        var d = await Seed(env, s, t); await PostStock(env, d, t); var api = Get<IInventoryApiClient>(env);
        if (method == "GetStockBalancesAsync") Assert.Contains(Ok(await api.GetStockBalancesAsync(new StockBalanceQuery(d.WarehouseId, d.ProductId, null, 1, 20), t)).Items, x => x.ProductId == d.ProductId && x.Quantity == 10m);
        else if (method == "GetStockCardAsync") Assert.Contains(Ok(await api.GetStockCardAsync(new StockCardQuery(d.WarehouseId, d.ProductId, null, null, 1, 20), t)).Items, x => x.ProductId == d.ProductId && x.QuantityChange == 10m);
        else throw Unknown(method);
    }

    private static async Task Identity(string method, Vps03OwnedEnvironment env, CancellationToken t)
    {
        var api = Get<IIdentityApiClient>(env);
        if (method == "LoginAsync") Session(env).SignOut();
        var response = method == "LoginAsync" ? await api.LoginAsync(new LoginRequest(env.Username, env.Password), t)
            : method == "GetCurrentUserAsync" ? await api.GetCurrentUserAsync(t) : throw Unknown(method);
        Assert.Equal(env.Username, Ok(response).Username);
    }

    private static async Task Security(string method, Vps03OwnedEnvironment env, string s, CancellationToken t)
    {
        var api = Get<ISecurityAdministrationApiClient>(env).ForSession(Session(env).Version);
        if (method == "ListUsersAsync") { Assert.Contains(Ok(await api.ListUsersAsync(t)), x => x.Username == env.Username); return; }
        if (method == "ListRolesAsync") { Ok(await api.ListRolesAsync(t)); return; }
        if (method == "MutateAsync") { Assert.True(Ok(await api.MutateAsync(new SecurityAdministrationMutationRequest(SecurityAdministrationTargetDto.Role, SecurityAdministrationOperationDto.Create, name: "VPS03-" + s), t)).Changed); return; }
        var roleName = "VPS03-IMPORT-" + s;
        Ok(await api.MutateAsync(new SecurityAdministrationMutationRequest(
            SecurityAdministrationTargetDto.Role, SecurityAdministrationOperationDto.Create, name: roleName), t));
        var role = Assert.Single(Ok(await api.ListRolesAsync(t)), x => x.Name == roleName);
        var json = "{\"schemaVersion\":1,\"targetType\":\"Role\",\"targetId\":" + role.RoleId + ",\"entries\":[]}";
        var preview = Ok(await api.PreviewPermissionImportAsync(PermissionImportTargetTypeDto.Role, role.RoleId, json, t));
        if (method == "PreviewPermissionImportAsync") { Assert.Equal(role.RoleId, preview.Import.TargetId); return; }
        if (method == "ApplyPermissionImportAsync") { Ok(await api.ApplyPermissionImportAsync(PermissionImportTargetTypeDto.Role, role.RoleId, new PermissionAssignmentImportRequest(json, preview.TargetRevision, preview.CatalogDigest), t)); return; }
        throw Unknown(method);
    }

    private static async Task<SeedData> Seed(Vps03OwnedEnvironment env, string s, CancellationToken t) => new(
        Ok(await Get<IProductApiClient>(env).CreateAsync(ProductRequest("P" + s), t)).Id,
        Ok(await Get<IWarehouseApiClient>(env).CreateAsync(WarehouseRequest("W" + s), t)).Id,
        Ok(await Get<IEmployeeApiClient>(env).CreateAsync(EmployeeRequest("E" + s), t)).Id,
        Ok(await Get<ICustomerApiClient>(env).CreateAsync(CustomerRequest("C" + s), t)).Id);
    private static async Task PostStock(Vps03OwnedEnvironment env, SeedData d, CancellationToken t)
    {
        var api = Get<IGoodsReceiptApiClient>(env);
        var draft = Ok(await api.CreateDraftAsync(new SaveGoodsReceiptRequest(DateTime.Today, d.WarehouseId, d.EmployeeId, "VPS03 stock", new[] { new SaveGoodsReceiptLineRequest(d.ProductId, 10m, 5m) }), t));
        Ok(await api.PostAsync(new PostGoodsReceiptRequest(draft.Id, draft.Version), t));
    }

    private static T Get<T>(Vps03OwnedEnvironment env) where T : notnull => env.DesktopServices.GetRequiredService<T>();
    private static IDesktopSession Session(Vps03OwnedEnvironment env) => Get<IDesktopSession>(env);
    private static CatalogListRequest List() => new(null, 1, 20, true);
    private static SaveProductRequest ProductRequest(string c) => new(c, "Product " + c, "EA");
    private static SaveWarehouseRequest WarehouseRequest(string c) => new(c, "Warehouse " + c, "Address");
    private static SaveEmployeeRequest EmployeeRequest(string c) => new(c, "Employee " + c, "0900000000", null);
    private static SaveCustomerRequest CustomerRequest(string c) => new(c, "Customer " + c, "Address", "0900000000", null);
    private static InvalidOperationException Unknown(string method) => new("Unknown VPS-03 method " + method);
    private static T Ok<T>(ApiResponse<T> response)
    {
        Assert.True(response.IsSuccess, response.Error?.Code + ": " + response.Error?.Message);
        Assert.NotNull(response.Data);
        return response.Data!;
    }

    private sealed class SeedData
    {
        internal SeedData(int p, int w, int e, int c) { ProductId = p; WarehouseId = w; EmployeeId = e; CustomerId = c; }
        internal int ProductId { get; } internal int WarehouseId { get; } internal int EmployeeId { get; } internal int CustomerId { get; }
    }
}
