using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace MyDmsVn.Server.HttpHost;

internal static class V1ModelStateErrors
{
    internal static IActionResult Create(ActionContext context)
    {
        // Do not echo model binder/formatter exception text or untrusted keys.
        // A binder's request-level error takes precedence over field details.
        var keys = context.ModelState.Where(pair => pair.Value?.Errors.Count > 0)
            .Select(pair => pair.Key).ToArray();
        var field = keys.Contains("") ? null : keys.FirstOrDefault(key => key is "limit" or "search" or "fromDate" or "toDate" or "customerId" or "warehouseId" or
            "employeeId" or "status" or "productId" or "pageSize" or "cursor" or "saleId");
        var correlation = context.HttpContext.RequestServices
            .GetRequiredService<RequestIdentityAdmission>().CorrelationId;
        return V1ApiResults.Invalid(field, correlation);
    }
}

// MVC's unsupported-content-type filter normally emits a bare 415 before the
// automatic model-state filter. Preserve the v1 error shape for native inputs.
// No body-binding production route is migrated in this pilot.
internal sealed class V1UnsupportedContentTypeFilter : IActionFilter, IOrderedFilter
{
    public int Order => -3001;

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.Values.SelectMany(value => value.Errors)
            .Any(error => error.Exception is Microsoft.AspNetCore.Mvc.ModelBinding.UnsupportedContentTypeException))
            context.Result = V1ModelStateErrors.Create(context);
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
