using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03UnjournalledOutcome
{
    internal string Kind { get; set; } = "";
    internal string DispatchState { get; set; } = "";
    internal string AuthorizedState { get; set; } = "";
    internal string DeniedState { get; set; } = "";
    internal bool ReleasedAfterDispatch { get; set; }
    internal bool ReleasedAfterAuthorizedReadback { get; set; }
    internal bool ReleasedAfterDeniedReadback { get; set; }
    internal string OperationId { get; set; } = "";
    internal string Fingerprint { get; set; } = "";
    internal string IdentityAfterAuthorizedReadback { get; set; } = "";
    internal string IdentityAfterDeniedReadback { get; set; } = "";
    internal string FingerprintAfterAuthorizedReadback { get; set; } = "";
    internal string FingerprintAfterDeniedReadback { get; set; } = "";
}

internal sealed class Vps03UnjournalledRecoveryScenario : IAsyncDisposable
{
    private readonly Vps03OwnedEnvironment _environment;
    private readonly ProductDto _product;
    private readonly WindowsHttpPendingWriteStore _store;
    private bool _disposed;

    private Vps03UnjournalledRecoveryScenario(Vps03OwnedEnvironment environment,
        ProductDto product, WindowsHttpPendingWriteStore store)
    { _environment = environment; _product = product; _store = store; }

    internal static async Task<Vps03UnjournalledRecoveryScenario> CreateAsync(CancellationToken token)
    {
        var environment = await Vps03OwnedEnvironment.CreateAsync(13, token).ConfigureAwait(false);
        try
        {
            var store = new WindowsHttpPendingWriteStore(new HttpDesktopOptions(
                environment.Origin.GetLeftPart(UriPartial.Authority)));
            if (store.Load() != null) throw new InvalidOperationException("VPS03.UnjournalledPendingSlotNotClean");
            var identity = await environment.DesktopServices.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(environment.Username, environment.Password), token)
                .ConfigureAwait(false);
            if (!identity.IsSuccess) throw new InvalidOperationException(identity.Error?.Code ?? "VPS03.UnjournalledOwnerLoginFailed");
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 9);
            var product = await environment.DesktopServices.GetRequiredService<IProductApiClient>()
                .CreateAsync(new SaveProductRequest("U" + suffix, "Unjournalled " + suffix, "EA"), token)
                .ConfigureAwait(false);
            if (!product.IsSuccess || product.Data == null)
                throw new InvalidOperationException(product.Error?.Code ?? "VPS03.UnjournalledSeedFailed");
            return new Vps03UnjournalledRecoveryScenario(environment, product.Data, store);
        }
        catch
        {
            await environment.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    internal async Task<IReadOnlyList<Vps03UnjournalledOutcome>> RunAsync(CancellationToken token)
    {
        var catalog = await RunCatalogAsync(token).ConfigureAwait(false);
        var security = await RunSecurityAsync(token).ConfigureAwait(false);
        return new[] { catalog, security };
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        var pending = _store.Load();
        if (pending != null) _store.Clear(pending);
        await _environment.DisposeAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private async Task<Vps03UnjournalledOutcome> RunCatalogAsync(CancellationToken token)
    {
        var actor = await _environment.CreateActorAsync("Catalog",
            new[] { "Catalog.Products.Write", "Catalog.Products.Read" }, token).ConfigureAwait(false);
        var route = "/api/v1/catalog/products/" + _product.Id;
        using var client = CreateClient(route, HttpMethod.Put, actor);
        var response = await client.Operations.SendUnjournaledAsync<ProductDto>(
            "Catalog.Products.Update", _product.Id, HttpMethod.Put, route,
            new SaveProductRequest(_product.Code, _product.Name + " observed", _product.Unit),
            null, token).ConfigureAwait(false);
        var pending = RequirePending("Catalog.Products.Update");
        var authorized = await client.Operations.CheckAsync(token).ConfigureAwait(false);
        var afterAuthorized = RequirePending("Catalog.Products.Update");
        await _environment.SetActorGrantAsync(actor.UserId, "Catalog.Products.Read", false, token)
            .ConfigureAwait(false);
        var denied = await client.Operations.CheckAsync(token).ConfigureAwait(false);
        var afterDenied = RequirePending("Catalog.Products.Update");
        var forbiddenRelease = await client.Operations.AcceptCertifiedNotExecutedAsync(token)
            .ConfigureAwait(false);
        if (forbiddenRelease.FenceReleased) throw new InvalidDataException("VPS03.UnjournalledCatalogReleased");
        var result = Outcome(pending, response.Error?.Code, authorized, denied,
            afterAuthorized, afterDenied);
        _store.Clear(afterDenied);
        return result;
    }

    private async Task<Vps03UnjournalledOutcome> RunSecurityAsync(CancellationToken token)
    {
        var actor = await _environment.CreateActorAsync("Security",
            new[] { "Security.Users.Manage", "Security.Roles.Manage" }, token).ConfigureAwait(false);
        const string route = "/api/v1/security/mutations";
        using var client = CreateClient(route, HttpMethod.Post, actor);
        var name = "VPS03 Role " + Guid.NewGuid().ToString("N").Substring(0, 8);
        var response = await client.Operations.SendUnjournaledAsync<SecurityAdministrationResultDto>(
            "Security.Mutate", null, HttpMethod.Post, route,
            new SecurityAdministrationMutationRequest(SecurityAdministrationTargetDto.Role,
                SecurityAdministrationOperationDto.Create, name: name), null, token)
            .ConfigureAwait(false);
        var pending = RequirePending("Security.Mutate");
        var authorized = await client.Operations.CheckAsync(token).ConfigureAwait(false);
        var afterAuthorized = RequirePending("Security.Mutate");
        await _environment.SetActorGrantAsync(actor.UserId, "Security.Users.Manage", false, token)
            .ConfigureAwait(false);
        var denied = await client.Operations.CheckAsync(token).ConfigureAwait(false);
        var afterDenied = RequirePending("Security.Mutate");
        var forbiddenRelease = await client.Operations.AcceptCertifiedNotExecutedAsync(token)
            .ConfigureAwait(false);
        if (forbiddenRelease.FenceReleased) throw new InvalidDataException("VPS03.UnjournalledSecurityReleased");
        var result = Outcome(pending, response.Error?.Code, authorized, denied,
            afterAuthorized, afterDenied);
        _store.Clear(afterDenied);
        return result;
    }

    private Vps03UnjournalledClient CreateClient(string droppedRoute, HttpMethod method,
        Vps03RecoveryActor actor)
    {
        var options = new HttpDesktopOptions(_environment.Origin.GetLeftPart(UriPartial.Authority));
        var session = new HttpDesktopSession(DesktopSessionOptions.Default, new Vps03ScenarioClock());
        var handler = new Vps03LostResponseHandler(droppedRoute, method);
        var transport = new HttpDesktopTransport(options, session, handler);
        var operations = new HttpDesktopOperations(transport, session,
            new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5), _store);
        var login = transport.LoginAsync(new LoginRequest(actor.Username, actor.Password), CancellationToken.None)
            .GetAwaiter().GetResult();
        if (!login.IsSuccess) throw new InvalidOperationException(login.Error?.Code ?? "VPS03.UnjournalledActorLoginFailed");
        return new Vps03UnjournalledClient(transport, operations);
    }

    private HttpPendingWriteRecord RequirePending(string kind)
    {
        var pending = _store.Load() ?? throw new InvalidDataException("VPS03.UnjournalledFenceMissing");
        if (pending.Protocol != "readback" || pending.Kind != kind)
            throw new InvalidDataException("VPS03.UnjournalledIdentityMismatch");
        return pending;
    }

    private static Vps03UnjournalledOutcome Outcome(HttpPendingWriteRecord pending,
        string? dispatchCode, DesktopWriteRecoveryResult authorized,
        DesktopWriteRecoveryResult denied, HttpPendingWriteRecord afterAuthorized,
        HttpPendingWriteRecord afterDenied) => new()
    {
        Kind = pending.Kind,
        DispatchState = dispatchCode == "Operation.OutcomeUnknown" ? "UNKNOWN" : dispatchCode ?? "",
        AuthorizedState = authorized.State,
        DeniedState = denied.State,
        ReleasedAfterDispatch = false,
        ReleasedAfterAuthorizedReadback = authorized.FenceReleased,
        ReleasedAfterDeniedReadback = denied.FenceReleased,
        OperationId = pending.OperationId,
        Fingerprint = pending.Fingerprint,
        IdentityAfterAuthorizedReadback = afterAuthorized.OperationId,
        IdentityAfterDeniedReadback = afterDenied.OperationId,
        FingerprintAfterAuthorizedReadback = afterAuthorized.Fingerprint,
        FingerprintAfterDeniedReadback = afterDenied.Fingerprint,
    };
}

internal sealed class Vps03UnjournalledClient : IDisposable
{
    private readonly HttpDesktopTransport _transport;
    internal Vps03UnjournalledClient(HttpDesktopTransport transport, HttpDesktopOperations operations)
    { _transport = transport; Operations = operations; }
    internal HttpDesktopOperations Operations { get; }
    public void Dispose() => _transport.Dispose();
}

internal sealed class Vps03LostResponseHandler : DelegatingHandler
{
    private readonly string _route;
    private readonly HttpMethod _method;
    private int _dropped;

    internal Vps03LostResponseHandler(string route, HttpMethod method)
        : base(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(), UseCookies = true,
            AllowAutoRedirect = false, UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.None,
        })
    { _route = route; _method = method; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (request.Method == _method && request.RequestUri?.AbsolutePath == _route &&
            Interlocked.CompareExchange(ref _dropped, 1, 0) == 0)
        {
            response.Dispose();
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":\"Proxy.AckLost\",\"message\":\"Unavailable\",\"details\":[]}}",
                    Encoding.UTF8, "application/json"),
            };
        }
        return response;
    }
}

internal sealed class Vps03ScenarioClock : IMonotonicClock
{
    public TimeSpan Elapsed => TimeSpan.Zero;
}
