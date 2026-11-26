using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace MyDmsVn.Server.HttpHost.Features.Security;

public sealed class PermissionCheckQuery
{
    [FromQuery(Name = "key"), Required, MaxLength(100)]
    public string Key { get; init; } = "";
}

public sealed class FoundationStatusQuery
{
    [FromQuery(Name = "clientName"), Required, MaxLength(128)]
    public string ClientName { get; init; } = "";
}

internal sealed class SecurityQueryBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(PermissionCheckQuery) ||
        context.Metadata.ModelType == typeof(FoundationStatusQuery) ? new SecurityQueryBinder() : null;
}

internal sealed class SecurityQueryBinder : IModelBinder
{
    public async Task BindModelAsync(ModelBindingContext context)
    {
        try
        {
            // The two named-query routes historically check body before query.
            var request = context.HttpContext.Request;
            await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
            var permission = context.ModelType == typeof(PermissionCheckQuery);
            var field = permission ? "key" : "clientName";
            var query = V1QueryPolicy.Read(request, field);
            var value = V1QueryPolicy.OptionalText(query, field, permission ? 100 : 128);
            if (string.IsNullOrWhiteSpace(value)) throw new V1BindingException(field);
            context.Result = ModelBindingResult.Success(permission
                ? (object)new PermissionCheckQuery { Key = value }
                : new FoundationStatusQuery { ClientName = value });
        }
        catch (V1BindingException error) { V1ScalarBinders.Fail(context, error); }
    }
}

// Core admission and the Desktop switch precede MVC; for the other five
// Security actions, legacy checks the query before reading the body.
// Applied explicitly so the two named-query routes keep their own precedence.
[AttributeUsage(AttributeTargets.Method)]
internal sealed class V1SecurityNoQueryAttribute : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        try
        {
            var request = context.HttpContext.Request;
            V1QueryPolicy.Read(request);
            if (request.Method == "GET")
                await V1QueryPolicy.RequireEmptyBodyAsync(request).ConfigureAwait(false);
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
