using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace MyDmsVn.Server.HttpHost.Features.Catalog;

// Host-owned typed DTO; query metadata is also available to MVC ApiExplorer.
public sealed class CatalogLookupQuery
{
    [FromQuery(Name = "search"), MaxLength(256)]
    public string? Search { get; init; }

    [FromQuery(Name = "limit"), BindRequired, Required, Range(1, 200)]
    public int Limit { get; init; }
}

internal sealed class CatalogLookupQueryBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(CatalogLookupQuery) ? new CatalogLookupQueryBinder() : null;
}

internal sealed class CatalogLookupQueryBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
        try
        {
            var request = bindingContext.HttpContext.Request;
            await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
            var query = V1QueryPolicy.Read(request, "search", "limit");
            // Preserve legacy error precedence: missing limit, invalid search, invalid limit.
            if (!query.TryGetValue("limit", out var limit)) throw new V1BindingException("limit");
            var search = V1QueryPolicy.OptionalText(query, "search", 256);
            bindingContext.Result = ModelBindingResult.Success(new CatalogLookupQuery
            {
                Search = search,
                Limit = V1QueryPolicy.PositiveInt(limit, "limit", 200)
            });
        }
        catch (V1BindingException error)
        {
            bindingContext.ModelState.TryAddModelError(error.Field ?? "", "The field or request format is invalid.");
            bindingContext.Result = ModelBindingResult.Failed();
        }
    }
}
