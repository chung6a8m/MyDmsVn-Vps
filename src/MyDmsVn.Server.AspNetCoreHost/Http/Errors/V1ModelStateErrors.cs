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
            "employeeId" or "status" or "productId" or "pageSize" or "cursor" or "saleId" or "receiptId" or "pageNumber" or "fromUtc" or "toUtc" or "id" or "includeInactive" or "key" or "clientName");
        var mutationError = keys.FirstOrDefault(key => key.StartsWith(V1MutationRequestFormatter.ErrorPrefix, System.StringComparison.Ordinal));
        if (mutationError != null)
        {
            var path = mutationError.Substring(V1MutationRequestFormatter.ErrorPrefix.Length);
            field = path.Length == 0 ? null : path;
        }
        var correlation = context.HttpContext.RequestServices
            .GetRequiredService<RequestIdentityAdmission>().CorrelationId;
        // Auth's frozen validation envelope has no field detail.
        if (context.ActionDescriptor is Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor action &&
            action.ControllerTypeInfo.AsType() == typeof(Features.Auth.AuthController))
            return V1ApiResults.Error(400, "ValidationError", "The request is invalid.", correlation);
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
