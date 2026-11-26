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
        Net10RouteTable.Map(app);
        Net10RouteTable.MapFallback(app);
        return app;
    }

    private static Endpoint[] Endpoints(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).ToArray();

    [Fact]
    public void Actual_endpoints_have_four_MVC_and_53_bridge_owners()
    {
        using var app = Build();
        var endpoints = Endpoints(app);
        var inventory = Net10RouteTable.ReadInventory(endpoints);
        Assert.Equal(57, inventory.Count);
        Assert.Equal(4, inventory.Count(row => row.ActiveOwner == "native"));
        Assert.Equal(53, inventory.Count(row => row.ActiveOwner == "bridge"));
        Assert.Equal(4, endpoints.Count(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() != null));
        Assert.All(inventory.Where(row => row.ActiveOwner == "native"), row =>
        {
            Assert.Equal("CatalogLookupController", row.Controller);
            Assert.Equal("MyDmsVn.Server.HttpHost.Features.Catalog.CatalogLookupController", row.ActualController);
            Assert.Equal(row.Action, row.ActualAction);
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("order")]
    [InlineData("verb")]
    [InlineData("owner")]
    public void Mixed_inventory_rejects_actual_endpoint_drift(string mutation)
    {
        using var app = Build();
        var endpoints = Endpoints(app);
        var target = Assert.Single(endpoints.OfType<RouteEndpoint>(), e =>
            e.Metadata.GetMetadata<ControllerActionDescriptor>()?.MethodInfo.Name == "Products");
        var remaining = endpoints.Where(endpoint => !ReferenceEquals(endpoint, target));
        var builder = new RouteEndpointBuilder(target.RequestDelegate!, target.RoutePattern,
            mutation == "order" ? 1 : target.Order);
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
            _ => remaining.Append(builder.Build())
        };
        Assert.Throws<InvalidOperationException>(() => Net10RouteTable.ReadInventory(mutated));
    }
}
