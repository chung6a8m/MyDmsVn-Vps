using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;
using Newtonsoft.Json;

namespace MyDmsVn.Vps03.RecoveryClient;

internal static class Program
{
    private const int MaximumExchangeBytes = 4096;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    private static int Main() => RunAsync().GetAwaiter().GetResult();

    private static async Task<int> RunAsync()
    {
        try
        {
            var mode = Required("MYDMSVN_VPS03_RECOVERY_MODE");
            if (mode != "post-lost-ack" && mode != "recover") return 64;
            var origin = Required("MYDMSVN_VPS03_RECOVERY_ORIGIN");
            var username = Required("MYDMSVN_VPS03_RECOVERY_USERNAME");
            var password = Required("MYDMSVN_VPS03_RECOVERY_PASSWORD");
            var exchange = Required("MYDMSVN_VPS03_RECOVERY_EXCHANGE");
            if (!Path.IsPathRooted(exchange) || !Directory.Exists(exchange)) return 64;

            var options = new HttpDesktopOptions(origin);
            CountingHandler handler;
            if (mode == "post-lost-ack")
            {
                var saleId = long.Parse(Required("MYDMSVN_VPS03_RECOVERY_SALE_ID"),
                    NumberStyles.None, CultureInfo.InvariantCulture);
                handler = new CountingHandler("/api/v1/sales-receipts/" +
                    saleId.ToString(CultureInfo.InvariantCulture) + "/post", dropResponse: true);
            }
            else handler = new CountingHandler(null, dropResponse: false);

            var services = new ServiceCollection();
            services.AddSingleton<IHttpPendingWriteStore>(new WindowsHttpPendingWriteStore(options));
            services.AddSingleton<HttpDesktopTransport>(provider => new HttpDesktopTransport(
                options, provider.GetRequiredService<HttpDesktopSession>(), handler));
            services.AddHttpDesktopAdapter(options,
                new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5));
            using var provider = services.BuildServiceProvider();
            var login = await provider.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(username, password), CancellationToken.None)
                .ConfigureAwait(false);
            if (!login.IsSuccess) throw new InvalidOperationException("Recovery.LoginRejected");
            var session = provider.GetRequiredService<IDesktopSession>();
            var recovery = provider.GetRequiredService<IDesktopWriteRecovery>();

            if (mode == "post-lost-ack")
            {
                var saleId = long.Parse(Required("MYDMSVN_VPS03_RECOVERY_SALE_ID"),
                    NumberStyles.None, CultureInfo.InvariantCulture);
                var version = Required("MYDMSVN_VPS03_RECOVERY_SALE_VERSION");
                var response = await provider.GetRequiredService<ISalesReceiptApiClient>()
                    .ForSession(session.Version)
                    .PostAsync(new PostSalesReceiptRequest(saleId, version), CancellationToken.None)
                    .ConfigureAwait(false);
                if (response.IsSuccess || response.Error?.Code != "Operation.OutcomeUnknown" || recovery.Pending == null)
                    throw new InvalidOperationException("Recovery.ExpectedUnknown");
                Write(exchange, new Result
                {
                    Stage = "unknown",
                    State = "UNKNOWN",
                    OperationId = recovery.Pending.OperationId,
                    Fingerprint = recovery.Pending.Fingerprint,
                    BusinessRequests = handler.BusinessRequests,
                    DroppedResponses = handler.DroppedResponses,
                    StatusRequests = handler.StatusRequests,
                    SessionVersion = session.Version,
                    ClientProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                });
                Thread.Sleep(Timeout.Infinite);
                return 0;
            }

            var pendingId = recovery.Pending?.OperationId;
            var result = await recovery.CheckAsync(CancellationToken.None).ConfigureAwait(false);
            Write(exchange, new Result
            {
                Stage = "recovered",
                State = result.State,
                FenceReleased = result.FenceReleased,
                OperationId = pendingId,
                BusinessRequests = handler.BusinessRequests,
                DroppedResponses = handler.DroppedResponses,
                StatusRequests = handler.StatusRequests,
                SessionVersion = session.Version,
                ClientProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
            });
            return 0;
        }
        catch (Exception error)
        {
            var exchange = Environment.GetEnvironmentVariable("MYDMSVN_VPS03_RECOVERY_EXCHANGE");
            if (!string.IsNullOrWhiteSpace(exchange) && Directory.Exists(exchange))
            {
                try { Write(exchange, new Result { Stage = "failed", FailureCode = Category(error) }); }
                catch { }
            }
            return 2;
        }
    }

    private static string Required(string name) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? throw new InvalidDataException("Recovery.EnvironmentMissing")
            : Environment.GetEnvironmentVariable(name)!;

    private static void Write(string directory, Result result)
    {
        var bytes = Utf8.GetBytes(JsonConvert.SerializeObject(result, Formatting.None));
        if (bytes.Length <= 0 || bytes.Length > MaximumExchangeBytes) throw new InvalidDataException();
        var target = Path.Combine(directory, "result.json");
        var temp = target + ".tmp";
        File.WriteAllBytes(temp, bytes);
        if (File.Exists(target)) File.Delete(target);
        File.Move(temp, target);
    }

    private static string Category(Exception error) => error is InvalidOperationException invalid &&
        invalid.Message.StartsWith("Desktop.", StringComparison.Ordinal) ? invalid.Message :
        error is HttpRequestException ? "Recovery.HttpFailure" : "Recovery.ClientFailure";

    private sealed class CountingHandler : DelegatingHandler
    {
        private readonly string? _dropRoute;
        private readonly bool _dropResponse;
        private int _business;
        private int _dropped;
        private int _status;

        internal CountingHandler(string? dropRoute, bool dropResponse) : base(new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
            AllowAutoRedirect = false,
            UseDefaultCredentials = false,
            AutomaticDecompression = DecompressionMethods.None,
        })
        { _dropRoute = dropRoute; _dropResponse = dropResponse; }

        internal int BusinessRequests => Volatile.Read(ref _business);
        internal int DroppedResponses => Volatile.Read(ref _dropped);
        internal int StatusRequests => Volatile.Read(ref _status);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var business = request.Method == HttpMethod.Post && _dropRoute != null &&
                string.Equals(path, _dropRoute, StringComparison.Ordinal);
            if (business) Interlocked.Increment(ref _business);
            if (request.Method == HttpMethod.Get && path.StartsWith("/api/v2/operations/", StringComparison.Ordinal))
                Interlocked.Increment(ref _status);
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (_dropResponse && request.Method == HttpMethod.Post &&
                string.Equals(path, _dropRoute, StringComparison.Ordinal) && response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref _dropped);
                response.Dispose();
                throw new HttpRequestException("Recovery deterministic ACK loss.");
            }
            return response;
        }
    }

    private sealed class Result
    {
        public string? Stage { get; set; }
        public string? State { get; set; }
        public bool FenceReleased { get; set; }
        public string? OperationId { get; set; }
        public string? Fingerprint { get; set; }
        public int BusinessRequests { get; set; }
        public int DroppedResponses { get; set; }
        public int StatusRequests { get; set; }
        public long SessionVersion { get; set; }
        public int ClientProcessId { get; set; }
        public string? FailureCode { get; set; }
    }
}
