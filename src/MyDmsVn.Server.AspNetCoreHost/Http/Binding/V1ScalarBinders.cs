using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using MyDmsVn.Server.HttpHost.Features.Sales;

namespace MyDmsVn.Server.HttpHost;

// Only Sales read inputs are registered here. Other route families may use
// distinct grammar and must opt in rather than globally changing long binding.
internal sealed class V1SalesReadBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        if (context.Metadata.ModelType == typeof(SalesListQuery)) return new SalesListQueryBinder();
        if (context.Metadata.ModelType == typeof(long) &&
            context.Metadata.ParameterName == "saleId" && context.BindingInfo.BindingSource == BindingSource.Path)
            return new SalesIdBinder();
        return null;
    }
}

internal static class V1ScalarBinders
{
    internal static DateOnly CalendarDate(string text, string field)
    {
        if (text.Length != 10 || text[4] != '-' || text[7] != '-' ||
            text.Where((_, index) => index != 4 && index != 7).Any(c => c < '0' || c > '9') ||
            !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new V1BindingException(field);
        return value;
    }

    internal static long PositiveLong(string text, string field)
    {
        if (text.Length is < 1 or > 19 || text.Length > 1 && text[0] == '0' ||
            text.Any(c => c < '0' || c > '9') ||
            !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1)
            throw new V1BindingException(field);
        return value;
    }

    internal static void Fail(ModelBindingContext context, V1BindingException error)
    {
        context.ModelState.TryAddModelError(error.Field ?? "", "The field or request format is invalid.");
        context.Result = ModelBindingResult.Failed();
    }
}

internal sealed class SalesIdBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext bindingContext)
    {
        try
        {
            var request = bindingContext.HttpContext.Request;
            // Detail checks query, then body, then the ID (even for an invalid ID).
            V1QueryPolicy.Read(request);
            await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
            var text = request.RouteValues["saleId"] as string ?? "";
            bindingContext.Result = ModelBindingResult.Success(V1ScalarBinders.PositiveLong(text, "saleId"));
        }
        catch (V1BindingException error)
        {
            V1ScalarBinders.Fail(bindingContext, error);
        }
    }
}
