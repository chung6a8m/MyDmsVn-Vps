// VPS-04A: preserve legacy request admission/DI semantics in a native ASP.NET Core scope.
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Identity;

namespace MyDmsVn.Server.HttpHost;

internal static class Web2Pipeline
{
    internal const string ScopeKey = "mydmsvn.requestServices";
}

internal static class CoreRequestServices
{
    internal static IServiceCollection AddHttpRequestIdentity(this IServiceCollection services)
    {
        services.AddScoped<RequestIdentityAdmission>();
        var writer = services.LastOrDefault(d => d.ServiceType == typeof(ISecurityAuditWriter));
        if (writer?.ImplementationType != null)
            services.Replace(ServiceDescriptor.Scoped(typeof(ISecurityAuditWriter), writer.ImplementationType));
        services.Replace(ServiceDescriptor.Scoped<ICurrentUserAccessor>(p =>
            new HttpCurrentUserAccessor(p.GetRequiredService<RequestIdentityAdmission>().Current)));
        services.Replace(ServiceDescriptor.Scoped<IRequestCorrelationAccessor>(p =>
            new HttpCorrelationAccessor(p.GetRequiredService<RequestIdentityAdmission>().CorrelationId)));
        return services;
    }
}

internal sealed class RequestIdentityAdmission
{
    private CurrentUser? _current;
    private string? _correlation;
    public Guid? SessionId { get; private set; }
    public CurrentUser Current => _current ?? throw new InvalidOperationException("HTTP identity has not been admitted.");
    public string CorrelationId => _correlation ?? throw new InvalidOperationException("HTTP correlation was not set.");
    public void BeginCorrelation(string value)
    {
        if (_correlation != null) throw new InvalidOperationException("Correlation already initialized.");
        _correlation = value;
    }
    public void Admit(CurrentUser current, Guid? sessionId = null)
    {
        if (_current != null) throw new InvalidOperationException("Identity already admitted.");
        _current = current;
        SessionId = sessionId;
    }
}

public sealed class HttpCurrentUserAccessor : ICurrentUserAccessor
{
    public HttpCurrentUserAccessor(CurrentUser current) => Current = current ?? throw new ArgumentNullException(nameof(current));
    public CurrentUser Current { get; }
}

internal sealed class HttpCorrelationAccessor : IRequestCorrelationAccessor
{
    public HttpCorrelationAccessor(string correlationId) => CorrelationId = correlationId;
    public string CorrelationId { get; }
}
