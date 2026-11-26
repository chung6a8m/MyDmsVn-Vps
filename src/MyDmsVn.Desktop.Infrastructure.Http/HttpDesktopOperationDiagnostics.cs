using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MyDmsVn.Contracts;
using Serilog;
using Serilog.Formatting.Json;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    internal static class HttpDesktopOperationDiagnostics
    {
        private static readonly Regex ErrorCodePattern = new("\\A[A-Za-z][A-Za-z0-9_.]{0,95}\\z");
        private static readonly Lazy<ILogger> Logger = new Lazy<ILogger>(CreateLogger);

        internal static void RecordResponse(Guid operationId, int status, ApiError? error)
        {
            Write(logger => logger.Information(
                "Operation HTTP response {OperationId} {HttpStatus} {ErrorCode} {DetailCodes}",
                operationId, status, SafeCode(error?.Code),
                error?.Details.Take(8).Select(detail => SafeCode(detail.Code)).Distinct().ToArray()));
        }

        internal static void RecordDisposition(string operationId, string disposition)
        {
            if (Guid.TryParseExact(operationId, "D", out var id))
                Write(logger => logger.Information(
                    "Operation Desktop disposition {OperationId} {ClientDisposition}", id, disposition));
        }

        private static string? SafeCode(string? value) =>
            value == null || ErrorCodePattern.IsMatch(value) ? value : "UnrecognizedErrorCode";

        private static ILogger CreateLogger()
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(profile))
                throw new InvalidOperationException("Desktop diagnostics profile is unavailable.");
            return new LoggerConfiguration().WriteTo.File(new JsonFormatter(),
                Path.Combine(profile, "MyDmsVn-Vps", "Diagnostics", "operations-.jsonl"),
                rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7,
                fileSizeLimitBytes: 2 * 1024 * 1024, rollOnFileSizeLimit: true, shared: true)
                .CreateLogger();
        }

        private static void Write(Action<ILogger> write)
        {
            try { write(Logger.Value); }
            catch (Exception) { /* Preserve the original transport or fence outcome. */ }
        }
    }
}
