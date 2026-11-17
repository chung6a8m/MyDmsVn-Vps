using System;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>Public API origin only. Never accepts credentials, SQL connections or insecure TLS.</summary>
    public sealed class HttpDesktopOptions
    {
        public const string OriginVariable = "MYDMSVN_DESKTOP_API_ORIGIN";

        public HttpDesktopOptions(string origin, TimeSpan? requestTimeout = null)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
                origin != uri.GetLeftPart(UriPartial.Authority) ||
                uri.IsDefaultPort && origin.EndsWith(":443", StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "A canonical HTTPS origin without path, query, fragment or credentials is required.",
                    nameof(origin));
            }

            Origin = uri;
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
            if (RequestTimeout < TimeSpan.FromSeconds(2) ||
                RequestTimeout > TimeSpan.FromMinutes(2))
            {
                throw new ArgumentOutOfRangeException(nameof(requestTimeout));
            }
        }

        public Uri Origin { get; }
        public TimeSpan RequestTimeout { get; }

        public static HttpDesktopOptions FromEnvironment()
        {
            var value = Environment.GetEnvironmentVariable(OriginVariable);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Desktop.HttpOriginMissing");
            try
            {
                return new HttpDesktopOptions(value);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException("Desktop.HttpOriginInvalid", exception);
            }
        }
    }
}
