using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using NativeLookup = MyDmsVn.Server.HttpHost.Features.Catalog.CatalogLookupController;
using NativeSales = MyDmsVn.Server.HttpHost.Features.Sales.SalesReceiptsController;

namespace MyDmsVn.Server.HttpHost;

// Read actual route endpoints, not an inventory that merely reports the
// historical controller definitions. Compatibility identity is kept separately.
internal static class EndpointInventory
{
    internal sealed record Row(string HttpMethod, string Path, string Controller, string Action, int Order,
        string ActiveOwner, string ActualController, string ActualAction);

    private static Type? NativeOwner(Net10RouteTable.Route route) =>
        route.Controller == typeof(CatalogLookupController) ? typeof(NativeLookup) :
        route.Controller == typeof(SalesReceiptsController) ? typeof(NativeSales) : null;

    internal static bool IsNative(Net10RouteTable.Route route) => NativeOwner(route) != null;

    internal static IReadOnlyList<Row> Read(IEnumerable<Endpoint> endpoints,
        IReadOnlyList<Net10RouteTable.Route> expected)
    {
        var rows = new List<Row>();
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            // Only our explicitly marked auxiliary routes are exempt. A high
            // Order alone must never hide an additional business endpoint.
            if (endpoint.Metadata.GetMetadata<Net10RouteTable.AuxiliaryRoute>() != null)
            {
                if (endpoint.Order < int.MaxValue - 1 ||
                    endpoint.Metadata.GetMetadata<Net10RouteTable.Route>() != null ||
                    endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() != null ||
                    endpoint.Metadata.GetMetadata<IHttpMethodMetadata>() != null)
                    throw new InvalidOperationException("Invalid auxiliary route metadata.");
                continue;
            }
            var path = "/" + (endpoint.RoutePattern.RawText ?? "").TrimStart('/');
            var verbs = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods;
            if (verbs == null || verbs.Count != 1)
                throw new InvalidOperationException("A business endpoint lacks exactly one HTTP verb.");
            var verb = verbs[0];
            var route = expected.SingleOrDefault(r => r.Path == path && r.Verb == verb);
            if (route == null || endpoint.Order != route.Order)
                throw new InvalidOperationException("API route verb, template or order drift.");

            var bridge = endpoint.Metadata.GetMetadata<Net10RouteTable.Route>();
            var native = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if ((bridge == null) == (native == null))
                throw new InvalidOperationException("An API endpoint must have exactly one active owner.");
            var owner = native != null ? "native" : "bridge";
            if (IsNative(route))
            {
                if (native == null || native.ControllerTypeInfo.AsType() != NativeOwner(route) ||
                    native.MethodInfo.Name != route.Action.Name ||
                    endpoint.Metadata.GetMetadata<V1DesktopApiRequiredAttribute>() != null)
                    throw new InvalidOperationException("Native WEB2 ownership or feature gate drift.");
            }
            else if (bridge != route)
                throw new InvalidOperationException("Bridge route ownership drift.");

            rows.Add(new Row(verb, path, route.Controller.Name, route.Action.Name, endpoint.Order, owner,
                (native?.ControllerTypeInfo.AsType() ?? bridge!.Controller).FullName!,
                native?.MethodInfo.Name ?? bridge!.Action.Name));
        }
        if (rows.Count != expected.Count || rows.Select(row => (row.HttpMethod, row.Path)).Distinct().Count() != expected.Count)
            throw new InvalidOperationException("Missing or duplicate active API endpoints.");
        return rows.OrderBy(row => row.HttpMethod, StringComparer.Ordinal)
            .ThenBy(row => row.Path, StringComparer.Ordinal).ToArray();
    }

    internal static string ToJson(IEnumerable<Row> rows) =>
        JsonSerializer.Serialize(rows, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
