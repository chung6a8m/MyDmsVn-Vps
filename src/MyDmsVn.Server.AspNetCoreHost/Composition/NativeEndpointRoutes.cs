using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyDmsVn.Server.HttpHost;

// MVC owns all business endpoints. The only auxiliary endpoints preserve the
// frozen JSON 405/404 behavior; they never invoke business actions.
internal static class NativeEndpointRoutes
{
    internal sealed class AuxiliaryRoute { }

    internal static string ToInventoryJson()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Production",
            Args = Array.Empty<string>()
        });
        builder.Logging.ClearProviders();
        builder.Services.AddVps10NativeApi();
        using var app = builder.Build();
        Map(app);
        return EndpointInventory.ToJson(ReadInventory(((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)));
    }

    internal static IReadOnlyList<EndpointInventory.Row> ReadInventory(IEnumerable<Endpoint> endpoints) =>
        EndpointInventory.Read(endpoints);

    internal static void Map(WebApplication app)
    {
        app.MapControllers();
        var actual = ReadInventory(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
        foreach (var group in actual.GroupBy(row => row.Path, StringComparer.Ordinal))
        {
            var accepted = group.Select(row => row.HttpMethod)
                .Distinct(StringComparer.Ordinal).OrderBy(verb => verb, StringComparer.Ordinal).ToArray();
            app.Map(group.Key, (HttpContext context) => MethodNotAllowedAsync(context, accepted))
                .WithOrder(int.MaxValue - 1).WithMetadata(new AuxiliaryRoute());
        }
        ReadInventory(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints));
    }

    internal static Task MethodNotAllowedAsync(HttpContext context, IReadOnlyList<string> allowed)
    {
        context.Response.Headers.Allow = string.Join(", ", allowed);
        return NativeHttpErrors.WriteAsync(context, 405, "Request.Invalid", "The request is invalid.",
            context.RequestServices.GetRequiredService<RequestIdentityAdmission>().CorrelationId);
    }

    internal static void MapFallback(WebApplication app) =>
        app.MapFallback("{**path}", (HttpContext context) => FallbackAsync(context))
            .WithMetadata(new AuxiliaryRoute());

    internal static Task FallbackAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
            return NativeHttpErrors.WriteAsync(context, 404, "NotFound", "The resource was not found.",
                context.RequestServices.GetRequiredService<RequestIdentityAdmission>().CorrelationId);
        context.Response.StatusCode = 404;
        return Task.CompletedTask;
    }
}
