using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.HttpHost.Features.Catalog;

namespace MyDmsVn.Server.HttpHost;

internal static class NativeApiRegistration
{
    internal static IServiceCollection AddVps10NativeApi(this IServiceCollection services)
    {
        services.AddControllers(options =>
            {
                options.ModelBinderProviders.Insert(0, new CatalogLookupQueryBinderProvider());
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
        return services;
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
