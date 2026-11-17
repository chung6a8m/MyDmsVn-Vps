using System;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    public enum HttpDesktopOperationMode
    {
        LegacyWeb11,
        Web4,
        Web5,
    }

    /// <summary>Explicitly pinned to the server/SQL deployment. No guessed downgrade.</summary>
    public sealed class HttpDesktopOperationSettings
    {
        public const string ModeVariable = "MYDMSVN_DESKTOP_OPERATION_MODE";

        public HttpDesktopOperationSettings(HttpDesktopOperationMode mode)
        {
            Mode = mode;
        }

        public HttpDesktopOperationMode Mode { get; }

        public static HttpDesktopOperationSettings FromEnvironment()
        {
            switch (Environment.GetEnvironmentVariable(ModeVariable))
            {
                case "legacy-web11": return new HttpDesktopOperationSettings(HttpDesktopOperationMode.LegacyWeb11);
                case "web4": return new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web4);
                case "web5": return new HttpDesktopOperationSettings(HttpDesktopOperationMode.Web5);
                default: throw new InvalidOperationException("Desktop.HttpOperationModeMissingOrInvalid");
            }
        }
    }
}
