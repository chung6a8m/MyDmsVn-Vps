using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Desktop.Infrastructure.Http;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03LostAckProxy : DelegatingHandler
{
    private readonly string _route;
    private int _matchedRequests;
    private int _droppedResponses;

    internal Vps03LostAckProxy(Uri origin, string route)
        : base(CreateNetworkHandler())
    {
        if (origin == null || origin.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("The lost-ACK boundary requires HTTPS.", nameof(origin));
        if (string.IsNullOrWhiteSpace(route) || !route.StartsWith("/api/v1/", StringComparison.Ordinal))
            throw new ArgumentException("A bounded API route is required.", nameof(route));
        _route = route;
    }

    internal int MatchedRequests => Volatile.Read(ref _matchedRequests);
    internal int DroppedResponses => Volatile.Read(ref _droppedResponses);

    internal static HttpMessageHandler CreatePassThrough() => CreateNetworkHandler();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var matches = request.Method == HttpMethod.Post &&
            string.Equals(request.RequestUri?.AbsolutePath, _route, StringComparison.Ordinal);
        if (matches) Interlocked.Increment(ref _matchedRequests);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!matches || !response.IsSuccessStatusCode) return response;
        Interlocked.Increment(ref _droppedResponses);
        response.Dispose();
        throw new HttpRequestException("VPS03 deterministic response drop after upstream completion.");
    }

    private static HttpClientHandler CreateNetworkHandler() => new HttpClientHandler
    {
        CookieContainer = new CookieContainer(),
        UseCookies = true,
        AllowAutoRedirect = false,
        UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.None,
    };
}

internal static class Vps03FaultedDesktopServices
{
    internal static ServiceProvider Create(Vps03OwnedEnvironment environment,
        HttpMessageHandler handler, IHttpPendingWriteStore store)
    {
        var options = new HttpDesktopOptions(environment.Origin.GetLeftPart(UriPartial.Authority));
        var services = new ServiceCollection();
        services.AddSingleton<IHttpPendingWriteStore>(store);
        services.AddSingleton<HttpDesktopTransport>(provider => new HttpDesktopTransport(
            options, provider.GetRequiredService<HttpDesktopSession>(), handler));
        services.AddHttpDesktopAdapter(options,
            new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5));
        return services.BuildServiceProvider();
    }
}

internal sealed class Vps03InMemoryPendingWriteStore : IHttpPendingWriteStore
{
    private readonly object _gate = new object();
    private HttpPendingWriteRecord? _record;

    public HttpPendingWriteRecord? Load()
    {
        lock (_gate) return _record;
    }

    public void Save(HttpPendingWriteRecord record)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        record.Validate();
        lock (_gate)
        {
            if (_record != null) throw new InvalidOperationException("VPS03 pending write already exists.");
            _record = record;
        }
    }

    public void Clear(HttpPendingWriteRecord expected)
    {
        if (expected == null) throw new ArgumentNullException(nameof(expected));
        lock (_gate)
        {
            if (_record?.OperationId != expected.OperationId ||
                _record.Fingerprint != expected.Fingerprint)
                throw new InvalidOperationException("VPS03 pending write changed.");
            _record = null;
        }
    }
}
