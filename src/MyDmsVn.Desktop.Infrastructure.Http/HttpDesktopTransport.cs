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

                _session.RequireReauthentication(generation);
                ClearRefreshCookie();
                return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Rotation might already have committed: never allow another
                // refresh on the old cookie after a caller-initiated cancellation.
                _session.RequireReauthentication(generation);
                ClearRefreshCookie();
                throw;
            }
            catch
            {
                _session.RequireReauthentication(generation);
                ClearRefreshCookie();
                return false;
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        private Task<ApiResponse<AuthTicket>> AuthAsync(
            string operation, object? body, CancellationToken token) =>
            RequestAsync<AuthTicket>(HttpMethod.Post, "/api/v1/auth/" + operation,
                body, null, cookieEndpoint: true, token);

        private async Task RevokeAsync(CancellationToken cancellationToken)
        {
            try
            {
                await RequestAsync<object>(HttpMethod.Post, "/api/v1/auth/logout",
                    null, null, cookieEndpoint: true, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ClearRefreshCookie();
            }
        }

        private async Task<ApiResponse<T>> RequestAsync<T>(
            HttpMethod method, string route, object? body, string? accessToken,
            bool cookieEndpoint, CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? additionalHeaders = null)
        {
            if (string.IsNullOrEmpty(route) || !route.StartsWith("/api/v1/", StringComparison.Ordinal) ||
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
                    request.Content = new StringContent(JsonConvert.SerializeObject(
                        body, ApiJson.CreateSerializerSettings()), Encoding.UTF8, "application/json");
                }

                try
                {
                    using (var response = await _client.SendAsync(
                        request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        if (response.Content?.Headers.ContentType?.MediaType != "application/json")
                            return InvalidResponse<T>();
                        var json = await ReadBoundedAsync(response.Content, cancellationToken)
                            .ConfigureAwait(false);
                        var status = (ApiStatusCode)(int)response.StatusCode;
                        ApiResponse<T> decoded;
                        try
                        {
                            json = HttpDesktopWire.NormalizeResponse<T>(json);
                            if (typeof(T) == typeof(Newtonsoft.Json.Linq.JObject) &&
                                response.IsSuccessStatusCode)
                            {
                                var envelope = HttpDesktopWire.ParseObjectPreservingDates(json);
                                if (envelope.Count != 1 ||
                                    !(envelope["data"] is Newtonsoft.Json.Linq.JObject objectData))
                                    return InvalidResponse<T>();
                                decoded = (ApiResponse<T>)(object)
                                    ApiResponse<Newtonsoft.Json.Linq.JObject>.Success(objectData);
                            }
                            else if (typeof(T) == typeof(UnitResponse))
                            {
                                var unit = ApiJson.DeserializeResponse<Newtonsoft.Json.Linq.JObject>(json, status);
                                if (unit.IsSuccess && unit.Data!.Count != 0)
                                    return InvalidResponse<T>();
                                decoded = unit.IsSuccess
                                    ? (ApiResponse<T>)(object)ApiResponse<UnitResponse>.Success(UnitResponse.Value)
                                    : ApiResponse<T>.Failure(unit.Error!);
                            }
                            else
                                decoded = ApiJson.DeserializeResponse<T>(json, status);
                        }
                        catch (Exception error) when (error is JsonException ||
                            error is ArgumentException || error is FormatException ||
                            error is OverflowException || error is NullReferenceException)
                        {
                            return InvalidResponse<T>();
                        }

                        if (decoded == null ||
                            (response.IsSuccessStatusCode != decoded.IsSuccess))
                            return InvalidResponse<T>();
                        return decoded;
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
