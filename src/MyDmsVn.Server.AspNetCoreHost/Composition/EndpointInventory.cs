using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;

namespace MyDmsVn.Server.HttpHost;

// Read the actual MVC endpoints; compatibility names are frozen data, not
// source-linked types. No reflection discovery or action invocation.
internal static class EndpointInventory
{
    internal sealed record Row(string HttpMethod, string Path, string Controller, string Action, int Order,
        string ActiveOwner, string ActualController, string ActualAction);

    internal static IReadOnlyList<Row> Read(IEnumerable<Endpoint> endpoints)
    {
        var expected = NativeRouteContract.Routes.ToDictionary(r => (r.Verb, r.Path));
        if (expected.Count != 57) throw new InvalidOperationException("Frozen API route inventory drift.");
        var rows = new List<Row>();
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            if (endpoint.Metadata.GetMetadata<NativeEndpointRoutes.AuxiliaryRoute>() != null)
            {
                if (endpoint.Order < int.MaxValue - 1 ||
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
            if (!expected.TryGetValue((verb, path), out var route) || endpoint.Order != route.Order)
                throw new InvalidOperationException("API route verb, template or order drift.");

            var native = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (native == null || native.ControllerTypeInfo.AsType().FullName != route.NativeController ||
                native.MethodInfo.Name != route.NativeAction ||
                (endpoint.Metadata.GetMetadata<V1DesktopApiRequiredAttribute>() != null) != route.DesktopApiRequired)
                throw new InvalidOperationException("Native API ownership or feature gate drift.");

            rows.Add(new Row(verb, path, route.BaselineController, route.BaselineAction, endpoint.Order,
                "native", native.ControllerTypeInfo.AsType().FullName!, native.MethodInfo.Name));
        }
        if (rows.Count != expected.Count || rows.Select(row => (row.HttpMethod, row.Path)).Distinct().Count() != expected.Count)
            throw new InvalidOperationException("Missing or duplicate active API endpoints.");
        return rows.OrderBy(row => row.HttpMethod, StringComparer.Ordinal)
            .ThenBy(row => row.Path, StringComparer.Ordinal).ToArray();
    }

    internal static string ToJson(IEnumerable<Row> rows) =>
        JsonSerializer.Serialize(rows, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
}
