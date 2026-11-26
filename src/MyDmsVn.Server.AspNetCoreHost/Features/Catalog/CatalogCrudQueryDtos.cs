using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.HttpHost.Features.Catalog;

public sealed class CatalogCrudListQuery
{
    [FromQuery(Name = "search"), MaxLength(256)]
    public string? Search { get; init; }
    [FromQuery(Name = "pageNumber"), Range(1, int.MaxValue)]
    public int PageNumber { get; init; } = 1;
    [FromQuery(Name = "pageSize"), Range(1, 200)]
    public int PageSize { get; init; } = 50;
    [FromQuery(Name = "includeInactive")]
    public bool IncludeInactive { get; init; }

    internal CatalogListRequest ToApplication() => new(Search, PageNumber, PageSize, IncludeInactive);
}

internal sealed class CatalogCrudQueryBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(CatalogCrudListQuery) ? new CatalogCrudQueryBinder() : null;
}

internal sealed class CatalogCrudQueryBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext context)
    {
        try
        {
            var request = context.HttpContext.Request;
            await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
            var query = V1QueryPolicy.Read(request, "search", "pageNumber", "pageSize", "includeInactive");
            var search = V1QueryPolicy.OptionalText(query, "search", 256);
            var page = query.TryGetValue("pageNumber", out var pageText)
                ? V1QueryPolicy.PositiveInt(pageText, "pageNumber", int.MaxValue) : 1;
            var size = query.TryGetValue("pageSize", out var sizeText)
                ? V1QueryPolicy.PositiveInt(sizeText, "pageSize", 200) : 50;
            var inactive = query.TryGetValue("includeInactive", out var inactiveText) && (inactiveText switch
            {
                "true" => true,
                "false" => false,
                _ => throw new V1BindingException("includeInactive")
            });
            context.Result = ModelBindingResult.Success(new CatalogCrudListQuery
                { Search = search, PageNumber = page, PageSize = size, IncludeInactive = inactive });
        }
        catch (V1BindingException error) { V1ScalarBinders.Fail(context, error); }
    }
}

// Resource filter runs after transport/auth/Desktop feature admission, before MVC
// body binding. Detail: query/body/id. Mutation: id/query/body. The :int route
// constraint is frozen and intentionally still selects 404 for non-int segments.
[AttributeUsage(AttributeTargets.Class)]
internal sealed class V1CatalogRouteAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        try
        {
            if (request.Method == "GET")
            {
                if (request.RouteValues.TryGetValue("id", out var id))
                {
                    V1QueryPolicy.Read(request);
                    await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
                    V1QueryPolicy.PositiveInt(id as string ?? "", "id", int.MaxValue);
                }
            }
            else
            {
                if (request.RouteValues.TryGetValue("id", out var id))
                    V1QueryPolicy.PositiveInt(id as string ?? "", "id", int.MaxValue);
                V1QueryPolicy.Read(request);
            }
        }
        catch (V1BindingException error)
        {
            context.Result = V1ApiResults.Invalid(error.Field, context.HttpContext.RequestServices
                .GetRequiredService<RequestIdentityAdmission>().CorrelationId);
            return;
        }
        await next().ConfigureAwait(false);
    }
}
