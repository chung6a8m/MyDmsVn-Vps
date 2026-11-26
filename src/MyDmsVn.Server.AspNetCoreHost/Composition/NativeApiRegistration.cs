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
using MyDmsVn.Server.HttpHost.Features.Inventory;

namespace MyDmsVn.Server.HttpHost;

internal static class NativeApiRegistration
{
    internal static IServiceCollection AddVps10NativeApi(this IServiceCollection services)
    {
        services.AddScoped<IV1OperationContextFactory, V1OperationContextFactory>();
        services.AddControllers(options =>
            {
                options.InputFormatters.Insert(0, new V1MutationRequestFormatter());
                options.ModelBinderProviders.Insert(0, new CatalogLookupQueryBinderProvider());
                options.ModelBinderProviders.Insert(0, new V1SalesReadBinderProvider());
                options.ModelBinderProviders.Insert(0, new V1Web3ReadBinderProvider());
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
                    context.JsonTypeInfo.Type == typeof(V1SuccessEnvelope<SalesListPageResponse>) ||
                    context.JsonTypeInfo.Type == typeof(V1SuccessEnvelope<SalesPostResponse>))
                {
                    schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
                    schema.Required.Add("data");
                }
                // ASP.NET Core 10 uses this metadata on generic nullable object
                // properties to create a oneOf(null, reference) after schema
                // transformers run. A successful Sales envelope always has data.
                if (context.JsonPropertyInfo?.Name == "data" &&
                    (context.JsonTypeInfo.Type == typeof(SalesDetailResponse) ||
                     context.JsonTypeInfo.Type == typeof(SalesListPageResponse) ||
                     context.JsonTypeInfo.Type == typeof(SalesPostResponse)))
                {
                    schema.Type = JsonSchemaType.Object;
                    schema.Metadata?.Remove("x-is-nullable-property");
                }
                V1Web3OpenApi.Describe(schema, context.JsonTypeInfo.Type, context.JsonPropertyInfo?.Name, context.ParameterDescription?.Name);
                DescribeSalesResponse(schema, context.JsonTypeInfo.Type);
                DescribeSalesRequest(schema, context.JsonTypeInfo.Type);
                // A URL segment is text. Avoid advertising a JSON integer that
                // JavaScript clients could round before formatting the path.
                if ((context.ParameterDescription?.Name is "saleId" or "receiptId") && context.JsonTypeInfo.Type == typeof(long))
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
                    "search is limited after trimming, cursor is bound to filters/pageSize, detail accepts a positive canonical Int64 path and no query/body. " +
                    "Sales writes accept strict application/json UTF-8, no query; decimal and Int64 wire fields are strings. " +
                    "WEB4-enabled writes require Idempotency-Key and X-MyDmsVn-Deployment-Epoch. " +
                    "WEB3 collection reads require pageNumber (1–2147483647) and pageSize (1–200). " +
                    "Receipt detail requires canonical positive Int64 path text; no query or body. " +
                    "Receipt writes accept strict application/json UTF-8, no query; quantity and unitCost are invariant decimal strings; " +
                    "expectedVersion is canonical Base64 of eight bytes. WEB3 output uses explicit nullable fields and fixed decimal scale. " +
                    "Stock card requires warehouseId/productId and accepts UTC Z instants only.";
                return Task.CompletedTask;
            });
        });
        return services;
    }

    // Input records use typed decimal/long values after binding; OpenAPI must
    // describe the actual JSON tokens rather than their CLR representation.
    private static void DescribeSalesRequest(OpenApiSchema schema, Type type)
    {
        if (schema.Properties == null) return;
        if (type == typeof(SalesCreateRequest) || type == typeof(SalesUpdateRequest) ||
            type == typeof(SalesPostRequest) || type == typeof(SalesSaveLineRequest))
        {
            schema.Required = new HashSet<string>(schema.Properties.Keys.Where(key => key != "note"), StringComparer.Ordinal);
            foreach (var key in new[] { "saleLineId", "qty", "unitPrice" })
                if (schema.Properties.TryGetValue(key, out var property) && property is OpenApiSchema scalar)
                {
                    scalar.Type = JsonSchemaType.String;
                    scalar.Format = null;
                    scalar.Pattern = key == "saleLineId" ? "^(0|[1-9][0-9]{0,18})$" : @"^[0-9]+(\.[0-9]{1,4})?$";
                    scalar.MaxLength = key == "saleLineId" ? 19 : 64;
                }
            if (schema.Properties.TryGetValue("note", out var note) && note is OpenApiSchema text)
                text.MaxLength = 1000;
            if (schema.Properties.TryGetValue("expectedVersion", out var version) && version is OpenApiSchema token)
                token.MaxLength = 128;
        }
        if (type == typeof(SalesPostResponse) && schema.Properties.TryGetValue("postedAtUtc", out var timestamp) &&
            timestamp is OpenApiSchema instant) instant.Format = "date-time";
    }

    // The Sales wire response DTOs are affected. Omitted nullable response
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
