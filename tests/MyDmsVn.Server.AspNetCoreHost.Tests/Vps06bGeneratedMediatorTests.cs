using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application;
using MyDmsVn.Server.Application.Identity;
using MyDmsVn.Server.Application.Security;
using MyDmsVn.Server.HttpHost;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

// These tests execute the actual generated product registration, not the isolated
// VPS-06A canary and not the net48 MediatR fixture graph.
public sealed class Vps06bGeneratedMediatorTests
{
    [Fact]
    public void Exactly_46_product_request_contracts_are_registered_once_as_scoped()
    {
        var services = CreateServices();
        var interfaces = typeof(GetFoundationStatusQuery).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>))
                .Select(i => (Handler: t, Contract: i)))
            .ToArray();

        Assert.Equal(46, interfaces.Length);
        Assert.Equal(34, interfaces.Select(i => i.Handler).Distinct().Count());
        Assert.Equal(46, interfaces.Select(i => i.Contract).Distinct().Count());
        foreach (var handler in interfaces)
        {
            var descriptor = Assert.Single(services, d => d.ServiceType == handler.Contract);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }
        Assert.Equal(ServiceLifetime.Scoped,
            Assert.Single(services, d => d.ServiceType == typeof(IMediator)).Lifetime);
    }

    [Fact]
    public async Task Generated_dispatch_preserves_typed_validation_and_error_field()
    {
        using var provider = CreateServices().BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IMediator>());
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var invalid = await mediator.Send(new GetFoundationStatusQuery(""), CancellationToken.None);
        Assert.True(invalid.IsError);
        Assert.Equal(ErrorType.Validation, invalid.FirstError.Type);
        Assert.Equal("Validation.Required", invalid.FirstError.Code);
        Assert.Equal("ClientName", invalid.FirstError.Metadata!["Field"]);

        var accepted = await mediator.Send(new GetFoundationStatusQuery("Desktop"), CancellationToken.None);
        Assert.False(accepted.IsError);
        Assert.True(accepted.Value.IsReady);
        Assert.Equal("Local", accepted.Value.Runtime);
    }

    [Fact]
    public async Task Generated_exception_pipeline_sanitizes_and_reports_once()
    {
        var services = CreateServices();
        var reporter = new CountingReporter();
        services.Replace(ServiceDescriptor.Singleton<IFoundationProbe, ExplodingProbe>());
        services.Replace(ServiceDescriptor.Singleton<IApplicationExceptionReporter>(reporter));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>()
            .Send(new GetFoundationStatusQuery("Desktop"), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Unexpected, result.FirstError.Type);
        Assert.Equal("InternalError", result.FirstError.Code);
        Assert.DoesNotContain("private SQL", result.FirstError.Description, StringComparison.Ordinal);
        Assert.Equal(1, reporter.Count);
    }

    [Fact]
    public async Task Cancellation_from_product_handler_is_propagated_not_sanitized()
    {
        using var provider = CreateServices().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IMediator>()
                .Send(new GetFoundationStatusQuery("Desktop"), cts.Token));
    }

    [Fact]
    public async Task Permission_and_identity_are_isolated_across_parallel_scopes()
    {
        var services = CreateServices();
        services.AddScoped<RequestState>();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUserAccessor>(p =>
            new ScopedCurrentUser(p.GetRequiredService<RequestState>())));
        services.Replace(ServiceDescriptor.Scoped<IPermissionAuthorizationService>(p =>
            new ScopedPermission(p.GetRequiredService<RequestState>())));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var stateA = first.ServiceProvider.GetRequiredService<RequestState>();
        var stateB = second.ServiceProvider.GetRequiredService<RequestState>();
        stateA.UserId = 31;
        stateA.Granted = true;
        stateB.UserId = 42;
        stateB.Granted = false;

        var a = first.ServiceProvider.GetRequiredService<IMediator>();
        var b = second.ServiceProvider.GetRequiredService<IMediator>();
        Assert.NotSame(a, b);
        var resultA = a.Send(new ListPermissionCatalogQuery(), CancellationToken.None).AsTask();
        var resultB = b.Send(new ListPermissionCatalogQuery(), CancellationToken.None).AsTask();
        await Task.WhenAll(resultA, resultB);

        Assert.False(resultA.Result.IsError);
        Assert.True(resultB.Result.IsError);
        Assert.Equal(ErrorType.Forbidden, resultB.Result.FirstError.Type);
        Assert.Equal("Auth.Forbidden", resultB.Result.FirstError.Code);
        Assert.Equal(31, stateA.LastAuthorizedActor);
        Assert.Equal(42, stateB.LastAuthorizedActor);
    }

    private static IServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddServerApplication();
        services.AddProductionGeneratedMediator();
        return services;
    }

    private sealed class ExplodingProbe : IFoundationProbe
    {
        public FoundationStatus GetStatus() => throw new InvalidOperationException("private SQL connection detail");
    }
    private sealed class CountingReporter : IApplicationExceptionReporter
    {
        public int Count { get; private set; }
        public void Report(Type type, Exception exception) => Count++;
    }
    private sealed class RequestState
    {
        public int UserId { get; set; }
        public bool Granted { get; set; }
        public int? LastAuthorizedActor { get; set; }
    }
    private sealed class ScopedCurrentUser : ICurrentUserAccessor
    {
        private readonly RequestState _state;
        public ScopedCurrentUser(RequestState state) => _state = state;
        public CurrentUser Current => CurrentUser.Authenticated(_state.UserId, "operator", "Operator", true);
    }
    private sealed class ScopedPermission : IPermissionAuthorizationService
    {
        private readonly RequestState _state;
        public ScopedPermission(RequestState state) => _state = state;
        public Task<bool> IsAllowedAsync(string permissionKey, CancellationToken cancellationToken)
            => IsAllowedAsync(_state.UserId, permissionKey, cancellationToken);
        public Task<bool> IsAllowedAsync(int userId, string permissionKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _state.LastAuthorizedActor = userId;
            return Task.FromResult(_state.Granted);
        }
    }
}
