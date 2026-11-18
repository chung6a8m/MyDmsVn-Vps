using Mediator;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.Application;

namespace MyDmsVn.Server.HttpHost;

// VPS-06B: source generation and registration belong to the outermost production
// executable, never Server.Application or the Windows/net48 graph.
internal static class GeneratedMediatorRegistration
{
    internal static IServiceCollection AddProductionGeneratedMediator(this IServiceCollection services)
    {
        services.AddMediator((MediatorOptions options) =>
        {
            options.Assemblies = [typeof(GetFoundationStatusQuery)];
            options.ServiceLifetime = ServiceLifetime.Scoped;
            // Generated registration preserves the original three-layer ordering.
            options.PipelineBehaviors =
            [
                typeof(ExceptionHandlingBehavior<,>),
                typeof(Security.AuthorizationBehavior<,>),
                typeof(ValidationBehavior<,>)
            ];
        });
        return services;
    }
}
