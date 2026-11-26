using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using MyDmsVn.Contracts;
using MyDmsVn.Server.HttpHost.Features.Catalog;
using MyDmsVn.Server.HttpHost.Features.Sales;

namespace MyDmsVn.Server.HttpHost;

internal static class NativeApiRegistration
{
    internal static IServiceCollection AddVps10NativeApi(this IServiceCollection services)
    {
        services.AddControllers(options =>
            {
                options.ModelBinderProviders.Insert(0, new CatalogLookupQueryBinderProvider());
                options.ModelBinderProviders.Insert(0, new V1SalesReadBinderProvider());
                options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
                options.Filters.Add(new V1UnsupportedContentTypeFilter());
            })
            .AddJsonOptions(options => V1HttpJson.Configure(options.JsonSerializerOptions))
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = V1ModelStateErrors.Create;
                options.SuppressMapClientErrors = true;
            })
            .ConfigureApplicationPartManager(manager =>
            {
                // Legacy types ending in Controller would otherwise satisfy
                // MVC naming conventions despite inheriting its shim ApiController.
                manager.ApplicationParts.Clear();
                manager.ApplicationParts.Add(new AssemblyPart(typeof(Program).Assembly));
                foreach (var provider in manager.FeatureProviders.OfType<ControllerFeatureProvider>().ToArray())
                    manager.FeatureProviders.Remove(provider);
                manager.FeatureProviders.Add(new NativeControllerFeatureProvider());
            });
        services.ConfigureHttpJsonOptions(options => V1HttpJson.Configure(options.SerializerOptions));
        services.AddOpenApi("v1-native", options =>
        {
            options.ShouldInclude = description => description.ActionDescriptor is ControllerActionDescriptor action &&
                typeof(ControllerBase).IsAssignableFrom(action.ControllerTypeInfo.AsType());
            // The successful Catalog lookup always contains a non-null array.
            // OpenAPI 3.1 can infer a nullable generic property; narrow only this
            // response schema, not the global serializer or unrelated DTOs.
            options.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type == typeof(V1SuccessEnvelope<IReadOnlyList<CatalogLookupDto>>))
                {
                    schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
                    schema.Required.Add("data");
                }
                if (context.JsonTypeInfo.Type == typeof(IReadOnlyList<CatalogLookupDto>) &&
                    context.JsonPropertyInfo?.Name == "data" &&
                    schema.Type == (JsonSchemaType.Array | JsonSchemaType.Null))
                    schema.Type = JsonSchemaType.Array;
                if (context.JsonTypeInfo.Type == typeof(V1SuccessEnvelope<SalesDetailResponse>) ||
                    context.JsonTypeInfo.Type == typeof(V1SuccessEnvelope<SalesListPageResponse>))
                {
                    schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
                    schema.Required.Add("data");
                }
                // ASP.NET Core 10 uses this metadata on generic nullable object
                // properties to create a oneOf(null, reference) after schema
                // transformers run. A successful Sales envelope always has data.
                if (context.JsonPropertyInfo?.Name == "data" &&
                    (context.JsonTypeInfo.Type == typeof(SalesDetailResponse) ||
                     context.JsonTypeInfo.Type == typeof(SalesListPageResponse)))
                {
                    schema.Type = JsonSchemaType.Object;
                    schema.Metadata?.Remove("x-is-nullable-property");
                }
                DescribeSalesResponse(schema, context.JsonTypeInfo.Type);
                // A URL segment is text. Avoid advertising a JSON integer that
                // JavaScript clients could round before formatting the path.
                if (context.ParameterDescription?.Name == "saleId" && context.JsonTypeInfo.Type == typeof(long))
                {
                    schema.Type = JsonSchemaType.String;
                    schema.Format = null;
                    schema.Pattern = "^[1-9][0-9]{0,18}$";
                    schema.MaxLength = 19;
                    schema.Description = "Canonical positive Int64 text, at most 9223372036854775807.";
                }
                return Task.CompletedTask;
            });
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "MyDmsVn native v1 API";
                document.Info.Description = "Partial native surface only. Legacy bridge endpoints are intentionally excluded. " +
                    "Lookups require Bearer authentication with an active session. " +
                    "No request body; limit is a required canonical integer from 1 to 200. " +
                    "Search is optional (maximum 256 UTF-16 code units); duplicate and unknown query names are rejected. " +
                    "Sales reads require an active Bearer session; list pageSize defaults to 25 (1–200), dates are calendar-only, " +
                    "search is limited after trimming, cursor is bound to filters/pageSize, detail accepts a positive canonical Int64 path and no query/body.";
                return Task.CompletedTask;
            });
        });
        return services;
    }

    // Only the four Sales wire DTOs are affected. Omitted nullable response
    // fields are optional, but if present they are strings, never JSON null.
    private static void DescribeSalesResponse(OpenApiSchema schema, Type type)
    {
        string[]? optional = type == typeof(SalesDetailResponse)
            ? new[] { "note", "postedAtUtc", "totalCost", "grossProfit", "customerDisplayName", "warehouseDisplayName", "employeeDisplayName" }
            : type == typeof(SalesDetailLineResponse)
                ? new[] { "postedUnitCost", "costAmount", "productDisplayName" }
                : type == typeof(SalesListPageResponse)
                    ? new[] { "nextCursor", "previousCursor" }
                    : type == typeof(SalesListRowResponse)
                        ? new[] { "customerDisplayName", "warehouseDisplayName", "employeeDisplayName" }
                        : null;
        if (optional == null || schema.Properties == null) return;
        schema.Required = new HashSet<string>(schema.Properties.Keys.Except(optional, StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var name in optional)
            if (schema.Properties.TryGetValue(name, out var property) && property is OpenApiSchema value)
            {
                value.Type = JsonSchemaType.String;
                if (name == "postedAtUtc") value.Format = "date-time";
            }
    }

    internal static void MapNativeOpenApi(WebApplication app)
    {
        // Diagnostic documentation only; never expose the partial document in Production.
        if (app.Environment.IsDevelopment())
            app.MapOpenApi("/openapi/{documentName}.json");
    }

    internal static async Task AdmitDesktopFeatureAsync(HttpContext context, Func<Task> next)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<V1DesktopApiRequiredAttribute>() != null &&
            !context.RequestServices.GetRequiredService<WebHostOptions>().DesktopApiEnabled)
        {
            var correlation = context.RequestServices.GetRequiredService<RequestIdentityAdmission>().CorrelationId;
            await V1ApiResults.Error(404, "NotFound", "The resource was not found.", correlation)
                .ExecuteResultAsync(new ActionContext(context, context.GetRouteData(), new ActionDescriptor()))
                .ConfigureAwait(false);
            return;
        }
        await next().ConfigureAwait(false);
    }

    private sealed class NativeControllerFeatureProvider : ControllerFeatureProvider
    {
        protected override bool IsController(TypeInfo typeInfo) =>
            typeof(ControllerBase).IsAssignableFrom(typeInfo.AsType()) && base.IsController(typeInfo);
    }
}

// Apply only to future additive Desktop API controllers/actions. Legacy
// WEB2 lookup routes deliberately do not require this feature switch.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
internal sealed class V1DesktopApiRequiredAttribute : Attribute { }
