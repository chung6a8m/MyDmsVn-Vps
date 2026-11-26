using System;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http.Wire;
using Newtonsoft.Json;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>
    /// Single HTTPS boundary for the Desktop; auth cookies are HttpOnly-equivalent
    /// client-side (never copied into DTOs, files or user-visible logs).
    /// No request, including a GET, is automatically replayed.
    /// </summary>
    public sealed class HttpDesktopTransport : IDisposable
    {
        private const int MaximumResponseBytes = 2 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly HttpDesktopOptions _options;
        private readonly HttpDesktopSession _session;
        private readonly HttpClient _client;
        private readonly CookieContainer? _cookies;
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);
        private readonly object _lifetime = new object();
        private bool _disposed;

        public HttpDesktopTransport(HttpDesktopOptions options, HttpDesktopSession session,
            HttpMessageHandler? handler = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _session = session ?? throw new ArgumentNullException(nameof(session));
            if (handler == null)
            {
                _cookies = new CookieContainer();
                handler = new HttpClientHandler
                {
                    CookieContainer = _cookies,
                    UseCookies = true,
                    AllowAutoRedirect = false,
                    UseDefaultCredentials = false,
                    AutomaticDecompression = DecompressionMethods.None,
                };
            }

            _client = new HttpClient(handler, disposeHandler: true)
            {
                BaseAddress = options.Origin,
                Timeout = options.RequestTimeout,
            };
            _session.RegisterRevoke(RevokeAsync);
        }

        public async Task<ApiResponse<CurrentUserDto>> LoginAsync(
            LoginRequest credentials, CancellationToken cancellationToken)
        {
            if (credentials == null) throw new ArgumentNullException(nameof(credentials));
            cancellationToken.ThrowIfCancellationRequested();
            if (!_session.TryBeginLogin(out var generation))
                return Fail<CurrentUserDto>(ApiStatusCode.Conflict, "Auth.AlreadyAuthenticated",
                    "Sign out before signing in as another user.");

            try
            {
                var result = await AuthAsync("login", credentials, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!result.IsSuccess)
                {
                    _session.CancelLogin(generation);
                    ClearRefreshCookie();
                    return ApiResponse<CurrentUserDto>.Failure(result.Error!);
                }

                var ticket = result.Data!;
                if (!ValidTicket(ticket) ||
                    !_session.TrySignIn(generation, ticket.User!, ticket.AccessToken!,
                        ticket.ExpiresAtUtc, cancellationToken))
                {
                    _session.CancelLogin(generation);
                    ClearRefreshCookie();
                    return Fail<CurrentUserDto>(ApiStatusCode.Unauthorized, "Auth.SessionChanged",
                        "The login response is no longer current. Sign in again.");
                }

                return ApiResponse<CurrentUserDto>.Success(ticket.User!);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _session.CancelLogin(generation);
                ClearRefreshCookie();
                throw;
            }
            catch
            {
                _session.CancelLogin(generation);
                ClearRefreshCookie();
                throw;
            }
        }

        public async Task<ApiResponse<T>> SendAsync<T>(
            HttpMethod method, string route, object? body, CancellationToken cancellationToken,
            long? expectedSessionVersion = null,
            IReadOnlyDictionary<string, string>? additionalHeaders = null)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            cancellationToken.ThrowIfCancellationRequested();
            if (!_session.TryCapture(expectedSessionVersion, out var generation,
                out var token, out var expiresUtc))
                return SessionChanged<T>();

            if (expiresUtc <= DateTime.UtcNow.AddSeconds(30))
            {
                if (!await RefreshAsync(generation, cancellationToken).ConfigureAwait(false))
                    return SessionChanged<T>();
                if (!_session.TryCapture(generation, out _, out token, out _))
                    return SessionChanged<T>();
            }

            var response = await RequestAsync<T>(method, route, body, token,
                cookieEndpoint: false, cancellationToken, additionalHeaders).ConfigureAwait(false);

            // A 401 means the authoritative host has rejected this session.
            // Never retry a mutation with new credentials or assume rollback.
            if (!response.IsSuccess && response.Error!.Status == ApiStatusCode.Unauthorized)
                _session.RequireReauthentication(generation);

            if (!_session.IsCurrent(generation))
                return SessionChanged<T>();

            return response;
        }

        public Task<ApiResponse<CurrentUserDto>> GetCurrentUserAsync(CancellationToken cancellationToken) =>
            SendAsync<CurrentUserDto>(HttpMethod.Get, "/api/v1/auth/me",
                null, cancellationToken);

        private async Task<bool> RefreshAsync(long generation, CancellationToken cancellationToken)
        {
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_session.TryCapture(generation, out _, out _, out var expiresUtc))
                    return false;
                if (expiresUtc > DateTime.UtcNow.AddSeconds(30))
                    return true;

                // SQL rotation can commit even if the ACK is lost. Exactly one
                // attempt: failure forces reauthentication, never replay refresh.
                var refreshed = await AuthAsync("refresh", null, cancellationToken)
                    .ConfigureAwait(false);
                if (refreshed.IsSuccess && ValidTicket(refreshed.Data!) &&
                    _session.TryRefresh(generation, refreshed.Data!.AccessToken!,
                        refreshed.Data.ExpiresAtUtc))
                    return true;

                DiscardRefresh(generation);
                return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Rotation might already have committed: never allow another
                // refresh on the old cookie after a caller-initiated cancellation.
                DiscardRefresh(generation);
                throw;
            }
            catch
            {
                DiscardRefresh(generation);
                return false;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private void DiscardRefresh(long generation)
        {
            _session.RequireReauthentication(generation);
            // Logout needs a refresh cookie from this family after rotation settles.
            if (_session.State != DesktopSessionState.LoggingOut)
                ClearRefreshCookie();
        }

        private Task<ApiResponse<AuthTicket>> AuthAsync(
            string operation, object? body, CancellationToken token) =>
            RequestAsync<AuthTicket>(HttpMethod.Post, "/api/v1/auth/" + operation,
                body, null, cookieEndpoint: true, token);

        private async Task RevokeAsync()
        {
            using var timeout = new CancellationTokenSource(_options.RequestTimeout);
            var entered = false;
            try
            {
                await _refreshGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                entered = true;
                var response = await RequestAsync<object>(HttpMethod.Post, "/api/v1/auth/logout",
                    null, null, cookieEndpoint: true, timeout.Token).ConfigureAwait(false);
                HttpDesktopOperationDiagnostics.RecordLogout(response.IsSuccess ? "Acknowledged" : "Unconfirmed",
                    response.Error?.Code);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                HttpDesktopOperationDiagnostics.RecordLogout("Unconfirmed", "Http.Timeout");
            }
            finally
            {
                ClearRefreshCookie();
                if (entered)
                    _refreshGate.Release();
            }
        }

        private async Task<ApiResponse<T>> RequestAsync<T>(
            HttpMethod method, string route, object? body, string? accessToken,
            bool cookieEndpoint, CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? additionalHeaders = null)
        {
            var exactWeb5Context = route == "/api/v2/operations/context";
            const string v2Prefix = "/api/v2/operations/";
            const string v2Suffix = "/resolution";
            var exactWeb5Resolution = false;
            if (route != null && route.StartsWith(v2Prefix, StringComparison.Ordinal) &&
                route.EndsWith(v2Suffix, StringComparison.Ordinal))
            {
                var idText = route.Substring(v2Prefix.Length,
                    route.Length - v2Prefix.Length - v2Suffix.Length);
                exactWeb5Resolution = Guid.TryParseExact(idText, "D", out var statusId) &&
                    idText == statusId.ToString("D") && idText[14] == '4' &&
                    "89ab".IndexOf(idText[19]) >= 0;
            }
            if (string.IsNullOrEmpty(route) ||
                (!route!.StartsWith("/api/v1/", StringComparison.Ordinal) &&
                 !exactWeb5Context && !exactWeb5Resolution) ||
                route.StartsWith("//", StringComparison.Ordinal) ||
                route.IndexOf('#') >= 0)
                throw new ArgumentException("An in-origin API v1 route is required.", nameof(route));
            var uri = new Uri(_options.Origin, route);
            if (!string.Equals(uri.GetLeftPart(UriPartial.Authority),
                _options.Origin.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal))
                throw new ArgumentException("Cross-origin requests are not supported.", nameof(route));

            using (var request = new HttpRequestMessage(method, uri))
            {
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.TryAddWithoutValidation("X-Correlation-ID", Guid.NewGuid().ToString("N"));
                if (method != HttpMethod.Get && method != HttpMethod.Head)
                    request.Headers.TryAddWithoutValidation("Origin", _options.Origin.ToString().TrimEnd('/'));
                if (cookieEndpoint)
                    request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
                if (accessToken != null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                if (additionalHeaders != null)
                {
                    foreach (var header in additionalHeaders)
                    {
                        if (header.Key != "Idempotency-Key" &&
                            header.Key != "X-MyDmsVn-Deployment-Epoch" &&
                            header.Key != "X-MyDmsVn-Operation-Fingerprint")
                            throw new InvalidOperationException("Unsupported operation header.");
                        if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value))
                            throw new InvalidOperationException("Invalid operation header.");
                    }
                }
                if (body != null)
                {
                    request.Content = new StringContent(V1WireMapper.SerializeRequest(body), Encoding.UTF8, "application/json");
                }

                try
                {
                    using (var response = await _client.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        var hasId = request.Headers.TryGetValues("Idempotency-Key", out var operationKeys) &&
                            operationKeys.Count() == 1;
                        var id = hasId && Guid.TryParseExact(operationKeys!.Single(), "D", out var parsedId)
                            ? parsedId : Guid.Empty;
                        ApiResponse<T> Observe(ApiResponse<T> result)
                        {
                            if (id != Guid.Empty)
                                HttpDesktopOperationDiagnostics.RecordResponse(id, (int)response.StatusCode, result.Error);
                            return result;
                        }

                        if (response.Content?.Headers.ContentType?.MediaType != "application/json")
                            return Observe(InvalidResponse<T>());
                        var json = await ReadBoundedAsync(response.Content, cancellationToken)
                            .ConfigureAwait(false);
                        var status = (ApiStatusCode)(int)response.StatusCode;
                        ApiResponse<T> decoded;
                        try
                        {
                            decoded = V1WireMapper.DecodeResponse<T>(json, status, response.IsSuccessStatusCode);
                        }
                        catch (Exception error) when (error is JsonException ||
                            error is ArgumentException || error is FormatException ||
                            error is OverflowException || error is NullReferenceException)
                        {
                            return Observe(InvalidResponse<T>());
                        }

                        if (decoded == null ||
                            (response.IsSuccessStatusCode != decoded.IsSuccess))
                            return Observe(InvalidResponse<T>());
                        return Observe(decoded);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (TaskCanceledException)
                {
                    return Fail<T>(ApiStatusCode.InternalServerError,
                        "Http.Timeout", "The server did not respond before the timeout.");
                }
                catch (HttpRequestException)
                {
                    return Fail<T>(ApiStatusCode.InternalServerError,
                        "Http.Unavailable", "The server is unavailable. Check the secure connection.");
                }
                catch (IOException)
                {
                    return Fail<T>(ApiStatusCode.InternalServerError,
                        "Http.Unavailable", "The server connection failed.");
                }
                catch (DecoderFallbackException)
                {
                    return InvalidResponse<T>();
                }
            }
        }

        private static async Task<string> ReadBoundedAsync(
            HttpContent content, CancellationToken cancellationToken)
        {
            if (content.Headers.ContentLength > MaximumResponseBytes)
                throw new IOException("HTTP response exceeded the size limit.");
            using (var source = await content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var destination = new MemoryStream())
            {
                var buffer = new byte[8192];
                while (true)
                {
                    var count = await source.ReadAsync(
                        buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
                    if (count == 0) break;
                    if (destination.Length + count > MaximumResponseBytes)
                        throw new IOException("HTTP response exceeded the size limit.");
                    destination.Write(buffer, 0, count);
                }

                return StrictUtf8.GetString(destination.ToArray());
            }
        }

        private static bool ValidTicket(AuthTicket? ticket) =>
            ticket != null && ticket.User != null && ticket.User.UserId > 0 &&
            !string.IsNullOrWhiteSpace(ticket.AccessToken) &&
            ticket.AccessToken!.Length <= 8192 && ticket.ExpiresAtUtc > DateTime.UtcNow;

        private void ClearRefreshCookie()
        {
            if (_cookies == null) return;
            foreach (Cookie cookie in _cookies.GetCookies(_options.Origin))
                cookie.Expired = true;
        }

        internal static ApiResponse<T> SessionChanged<T>() =>
            Fail<T>(ApiStatusCode.Unauthorized, "Auth.SessionChanged",
                "The session is no longer valid. Please sign in again.");

        private static ApiResponse<T> InvalidResponse<T>() =>
            Fail<T>(ApiStatusCode.InternalServerError, "Http.InvalidResponse",
                "The server returned an incompatible response.");

        internal static ApiResponse<T> Fail<T>(ApiStatusCode status, string code, string message) =>
            ApiResponse<T>.Failure(new ApiError(status, code, message));

        public void Dispose()
        {
            lock (_lifetime)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _client.Dispose();
            _refreshGate.Dispose();
        }

        private sealed class AuthTicket
        {
            public string? AccessToken { get; set; }
            public DateTime ExpiresAtUtc { get; set; }
            public CurrentUserDto? User { get; set; }
        }
    }
}
