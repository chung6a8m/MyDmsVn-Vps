using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MyDmsVn.Vps06A.MediatorCanary;

internal static class Program
{
    private static int _passed;

    public static async Task<int> Main()
    {
        var services = new ServiceCollection();
        services.AddScoped<CanaryScope>();
        services.AddScoped<ValidatorOverlapProbe>();
        services.AddScoped<IValidator<CanaryRequest>, PositiveQuantityValidator>();
        services.AddScoped<IValidator<CanaryRequest>, RequiredLineValidator>();
        // Compile-time parsed, isolated outermost generator boundary. The production ASP.NET
        // Core host must remain MediatR-based until VPS-06B.
        services.AddMediator((MediatorOptions options) =>
        {
            options.Assemblies = [typeof(CanaryRequest)];
            options.ServiceLifetime = ServiceLifetime.Scoped;
            options.PipelineBehaviors =
            [
                typeof(CanaryExceptionBehavior),
                typeof(CanaryAuthorizationBehavior),
                typeof(CanaryValidationBehavior)
            ];
        });

        try
        {
            using var provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

            await Check("generated scope descriptors", async () =>
            {
                var mediator = services.Single(x => x.ServiceType == typeof(IMediator));
                Require(mediator.Lifetime == ServiceLifetime.Scoped, "Mediator is not scoped");
                var handler = services.Single(x => x.ServiceType == typeof(IRequestHandler<CanaryRequest, ErrorOr<string>>));
                Require(handler.Lifetime == ServiceLifetime.Scoped, "Internal handler is not scoped");
                await Task.CompletedTask;
            });
            await Check("root scope refuses mediator", async () =>
            {
                RequireThrows<InvalidOperationException>(() => provider.GetRequiredService<IMediator>());
                await Task.CompletedTask;
            });
            await Check("internal handler dispatch; pipeline and audit order", async () =>
            {
                using var scope = provider.CreateScope();
                var state = scope.ServiceProvider.GetRequiredService<CanaryScope>();
                state.CorrelationId = "request-a";
                var request = new CanaryRequest(2.5m);
                var result = await Send(scope, request);
                Require(!result.IsError, "valid request returned error");
                Require(result.Value == $"101|request-a|{state.Id:D}", "actor or correlation mismatch");
                Require(request.AuthorizedUserId == 101, "actor not bound");
                Trace(state, "exception", "authorization", "audit-admitted", "validation",
                    "validator-positive", "validator-line", "handler");
                Require(state.Audits.SequenceEqual(new[] { "Admitted:101:request-a" }), "audit shape mismatch");
            });
            await Check("unauthenticated fails before validation; audit denied", async () =>
            {
                using var scope = provider.CreateScope();
                var state = scope.ServiceProvider.GetRequiredService<CanaryScope>();
                state.Authenticated = false;
                state.CorrelationId = "unauthenticated";
                var result = await Send(scope, new CanaryRequest(0m));
                Failure(result, ErrorType.Unauthorized, "Auth.Unauthorized");
                Trace(state, "exception", "authorization", "audit-denied");
                Require(state.Audits.SequenceEqual(new[] { "Denied:null:unauthenticated" }), "denial audit incorrect");
            });
            await Check("inactive current user is unauthorized", async () =>
            {
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<CanaryScope>().Active = false;
                Failure(await Send(scope, new CanaryRequest(0m)), ErrorType.Unauthorized, "Auth.Unauthorized");
            });
            await Check("missing user ID is unauthorized", async () =>
            {
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<CanaryScope>().UserId = null;
                Failure(await Send(scope, new CanaryRequest(0m)), ErrorType.Unauthorized, "Auth.Unauthorized");
            });
            await Check("forbidden fails before validation; audit denied", async () =>
            {
                using var scope = provider.CreateScope();
                var state = scope.ServiceProvider.GetRequiredService<CanaryScope>();
                state.Permission = false;
                Failure(await Send(scope, new CanaryRequest(0m)), ErrorType.Forbidden, "Auth.Forbidden");
                Trace(state, "exception", "authorization", "audit-denied");
                Require(state.Audits.Single().StartsWith("Denied:101:", StringComparison.Ordinal), "denied actor absent");
            });
            await Check("no-audit request does not write security audit", async () =>
            {
                using var scope = provider.CreateScope();
                var state = scope.ServiceProvider.GetRequiredService<CanaryScope>();
                var result = await Send(scope, new CanaryRequest(1m) { Audit = false });
                Require(!result.IsError && state.Audits.Count == 0, "unexpected audit");
                Trace(state, "exception", "authorization", "validation",
                    "validator-positive", "validator-line", "handler");
            });
            await Check("validators sequential, typed field metadata and no handler", async () =>
            {
                using var scope = provider.CreateScope();
                var state = scope.ServiceProvider.GetRequiredService<CanaryScope>();
                var result = await Send(scope, new CanaryRequest(0m));
                Require(result.IsError && result.Errors.Count == 2, "expected exactly two validation failures");
                Require(result.Errors[0].Code == "Validation.Positive" &&
                        (string)result.Errors[0].Metadata!["Field"] == "Lines[0].Quantity", "first field mismatch");
                Require(result.Errors[1].Code == "Validation.LineRequired" &&
                        (string)result.Errors[1].Metadata!["Field"] == "Lines[0]", "second field mismatch");
                Trace(state, "exception", "authorization", "audit-admitted", "validation",
                    "validator-positive", "validator-line");
                Require(!scope.ServiceProvider.GetRequiredService<ValidatorOverlapProbe>().Overlapped,
                    "scoped validators overlapped");
            });
            await Check("unexpected exception sanitized and reported once", async () =>
            {
                using var scope = provider.CreateScope();
                var state = scope.ServiceProvider.GetRequiredService<CanaryScope>();
                var result = await Send(scope, new CanaryRequest(1m) { Mode = "throw" });
                Failure(result, ErrorType.Unexpected, "InternalError");
                Require(!result.FirstError.Description.Contains("secret", StringComparison.OrdinalIgnoreCase),
                    "private diagnostic escaped");
                Require(state.ReportedExceptions == 1, "reporter missing or duplicate");
            });
            foreach (var item in new (string Code, ErrorType Type)[]
            {
                ("Auth.Unauthorized", ErrorType.Unauthorized),
                ("Auth.Forbidden", ErrorType.Forbidden),
                ("Idempotency.KeyConflict", ErrorType.Conflict),
                ("Deployment.EpochMismatch", ErrorType.Conflict),
                ("Operation.ResultExpired", ErrorType.Conflict),
                ("Operation.ResultUnavailable", ErrorType.Conflict),
                ("Operation.Unavailable", ErrorType.Unexpected)
            })
            {
                await Check("closed operation mapping: " + item.Code, async () =>
                {
                    using var scope = provider.CreateScope();
                    var state = scope.ServiceProvider.GetRequiredService<CanaryScope>();
                    var result = await Send(scope, new CanaryRequest(1m) { Mode = "operation", OperationCode = item.Code });
                    Failure(result, item.Type, item.Code);
                    Require(state.ReportedExceptions == 0, "controlled operation was reported unexpectedly");
                });
            }
            await Check("unknown operation diagnostic never disclosed", async () =>
            {
                using var scope = provider.CreateScope();
                var result = await Send(scope, new CanaryRequest(1m)
                    { Mode = "operation", OperationCode = "secret-operator-diagnostic" });
                Failure(result, ErrorType.Unexpected, "InternalError");
                Require(!result.FirstError.Description.Contains("secret", StringComparison.Ordinal), "diagnostic leak");
            });
            await Check("cancelled token rethrows without reporting", async () =>
            {
                using var scope = provider.CreateScope();
                using var cts = new CancellationTokenSource();
                cts.Cancel();
                var state = scope.ServiceProvider.GetRequiredService<CanaryScope>();
                await RequireThrowsAsync<OperationCanceledException>(
                    async () => await Send(scope, new CanaryRequest(1m) { Mode = "cancel" }, cts.Token));
                Require(state.ReportedExceptions == 0, "cancellation was reported as error");
            });
            await Check("scoped mediators and identity/correlation never cross requests", async () =>
            {
                using var first = provider.CreateScope();
                using var second = provider.CreateScope();
                var state1 = first.ServiceProvider.GetRequiredService<CanaryScope>();
                var state2 = second.ServiceProvider.GetRequiredService<CanaryScope>();
                state1.UserId = 7; state1.CorrelationId = "correlation-first";
                state2.UserId = 9; state2.CorrelationId = "correlation-second";
                var mediator1 = first.ServiceProvider.GetRequiredService<IMediator>();
                var mediator2 = second.ServiceProvider.GetRequiredService<IMediator>();
                Require(!ReferenceEquals(mediator1, mediator2), "scoped mediator reused across requests");
                var a = mediator1.Send(new CanaryRequest(1m)).AsTask();
                var b = mediator2.Send(new CanaryRequest(1m)).AsTask();
                await Task.WhenAll(a, b);
                Require(a.Result.Value == $"7|correlation-first|{state1.Id:D}", "first request leaked identity");
                Require(b.Result.Value == $"9|correlation-second|{state2.Id:D}", "second request leaked identity");
                Require(state1.Audits.Single() == "Admitted:7:correlation-first", "first audit crossed scope");
                Require(state2.Audits.Single() == "Admitted:9:correlation-second", "second audit crossed scope");
                Require(state1.Id != state2.Id, "scoped objects were shared");
            });
            Console.WriteLine($"VPS06A_CANARY_PASS {_passed}/{_passed}, generator=3.0.2, scoped=true");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("VPS06A_CANARY_FAIL: " + ex.GetType().Name + " (" + _passed + " passed)");
            return 1;
        }
    }

    private static async Task<ErrorOr<string>> Send(
        IServiceScope scope, CanaryRequest request, CancellationToken cancellationToken = default) =>
        await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request, cancellationToken);

    private static async Task Check(string name, Func<Task> action)
    {
        await action();
        _passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    private static void Trace(CanaryScope state, params string[] expected)
    {
        Require(state.Events.SequenceEqual(expected),
            "pipeline order changed: actual=" + string.Join(",", state.Events));
    }

    private static void Failure(ErrorOr<string> result, ErrorType type, string code)
    {
        Require(result.IsError, "expected typed ErrorOr failure");
        Require(result.FirstError.Type == type && result.FirstError.Code == code,
            "error code or type changed");
    }

    private static void RequireThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException("expected " + typeof(T).Name);
    }

    private static async Task RequireThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); }
        catch (T) { return; }
        throw new InvalidOperationException("expected " + typeof(T).Name);
    }
}
