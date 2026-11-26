using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.HttpHost;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps10EndpointInventoryTests
{
    private static WebApplication Build()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddVps10NativeApi();
        var app = builder.Build();
        NativeEndpointRoutes.Map(app);
        NativeEndpointRoutes.MapFallback(app);
        return app;
    }

    private static Endpoint[] Endpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();

    [Fact]
    public void Actual_endpoints_have_57_MVC_and_0_bridge_owners()
    {
        using var app = Build();
        var endpoints = Endpoints(app);
        var inventory = NativeEndpointRoutes.ReadInventory(endpoints);
        Assert.Equal(57, inventory.Count);
        Assert.Equal(57, inventory.Count(row => row.ActiveOwner == "native"));
        Assert.Equal(0, inventory.Count(row => row.ActiveOwner == "bridge"));
        Assert.Equal(57, endpoints.Count(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() != null));
        Assert.All(inventory.Where(row => row.ActiveOwner == "native"), row =>
        {
            var expected = row.Path.StartsWith("/api/v1/catalog/", StringComparison.Ordinal)
                ? "MyDmsVn.Server.HttpHost.Features.Catalog." + row.Controller
                : row.Path.StartsWith("/api/v1/goods-receipts", StringComparison.Ordinal)
                    ? "MyDmsVn.Server.HttpHost.Features.Receipts.GoodsReceiptsController"
                    : row.Path.StartsWith("/api/v1/inventory/", StringComparison.Ordinal)
                        ? "MyDmsVn.Server.HttpHost.Features.Inventory.InventoryController"
                        : row.Path.StartsWith("/api/v1/security/", StringComparison.Ordinal)
                            ? "MyDmsVn.Server.HttpHost.Features.Security.SecurityController"
                            : row.Path.StartsWith("/api/v1/foundation/", StringComparison.Ordinal)
                                ? "MyDmsVn.Server.HttpHost.Features.Foundation.FoundationController"
                                : row.Path.StartsWith("/api/v1/auth/", StringComparison.Ordinal)
                                    ? "MyDmsVn.Server.HttpHost.Features.Auth.AuthController"
                                    : row.Path == "/api/v1/health"
                                        ? "MyDmsVn.Server.HttpHost.Features.Auth.HealthController"
                                        : row.Path.StartsWith("/api/v1/operations/", StringComparison.Ordinal)
                                            ? "MyDmsVn.Server.HttpHost.Features.Operations.OperationsController"
                                            : row.Path.StartsWith("/api/v2/operations/", StringComparison.Ordinal)
                                                ? "MyDmsVn.Server.HttpHost.Features.Operations.ResolutionController"
                                                : row.Path.StartsWith("/api/v3/operations/", StringComparison.Ordinal)
                                                    ? "MyDmsVn.Server.HttpHost.Features.Operations.ResendController"
                                                    : "MyDmsVn.Server.HttpHost.Features.Sales.SalesReceiptsController";
            Assert.Equal(expected, row.ActualController);
            Assert.Equal(expected.Split('.').Last() switch
            {
                "SecurityController" => "SecurityParityController",
                "FoundationController" => "FoundationParityController",
                "ResolutionController" => "OperationResolutionController",
                "ResendController" => "Web6ResendEligibilityController",
                var name => name
            }, row.Controller);
            Assert.Equal(row.Action, row.ActualAction);
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("late-duplicate")]
    [InlineData("order")]
    [InlineData("verb")]
    [InlineData("owner")]
    public void Native_inventory_rejects_actual_endpoint_drift(string mutation)
    {
        using var app = Build();
        var endpoints = Endpoints(app);
        var target = Assert.Single(endpoints.OfType<RouteEndpoint>(), e =>
            e.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo.Name == "Products");
        var remaining = endpoints.Where(endpoint => !ReferenceEquals(endpoint, target));
        var builder = new RouteEndpointBuilder(target.RequestDelegate!, target.RoutePattern,
            mutation == "order" ? 1 : mutation == "late-duplicate" ? int.MaxValue - 1 : target.Order);
        foreach (var item in target.Metadata)
        {
            if (mutation == "owner" && item is ControllerActionDescriptor) continue;
            if (mutation == "verb" && item is IHttpMethodMetadata) continue;
            builder.Metadata.Add(item);
        }
        if (mutation == "verb") builder.Metadata.Add(new HttpMethodMetadata(new[] { "POST" }));
        var mutated = mutation switch
        {
            "missing" => remaining,
            "duplicate" => endpoints.Append(target),
            "late-duplicate" => endpoints.Append(builder.Build()),
            _ => remaining.Append(builder.Build())
        };
        Assert.Throws<InvalidOperationException>(() => NativeEndpointRoutes.ReadInventory(mutated));
    }
}
