using System;
using System.IO;
using System.Linq;
using MyDmsVn.Contracts;
using Serilog;
using Serilog.Formatting.Json;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    internal static class HttpDesktopOperationDiagnostics
    {
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

        internal static void RecordLogout(string disposition, string? errorCode) =>
            Write(logger => logger.Information("Desktop logout {ClientDisposition} {ErrorCode}",
                disposition, SafeCode(errorCode)));

        internal static void RecordRefresh(Guid correlationId, string disposition, int? httpStatus,
            string? errorCode, string? networkStatus) =>
            Write(logger => logger.Information(
                "Desktop refresh {CorrelationId} {ClientDisposition} {HttpStatus} {ErrorCode} {NetworkStatus}",
                correlationId.ToString("N"), disposition, httpStatus, RefreshErrorCode(errorCode), networkStatus));

        // Server error codes are untrusted text. Only emit codes understood by this flow.
        private static string? RefreshErrorCode(string? code) => code switch
        {
            null or "ValidationError" or "Auth.Unauthorized" or "Auth.RateLimited" or
                "Auth.InvalidRefreshTicket" or "Auth.SessionChanged" or "Http.InvalidResponse" or
                "Http.Timeout" or "Http.Unavailable" or "Refresh.Canceled" or "Refresh.Unexpected" => code,
            _ => "UnrecognizedErrorCode",
        };

        // Operation responses and their detail codes are also remote text.
        private static string? SafeCode(string? code) => code switch
        {
            "Auth.Forbidden" or "Auth.InvalidCredentials" or "Deployment.EpochMismatch" or
                "Idempotency.FingerprintMismatch" or "Idempotency.KeyConflict" or
                "Operation.NotObserved" or "Operation.ResultExpired" or "Operation.ResultUnavailable" or
                "Operation.Unavailable" => code,
            "Request.CustomerId" or "Request.EmployeeId" or "Request.Lines" or "Request.WarehouseId" or
                "Validation.Error" or "Validation.GreaterThan" or "Validation.GreaterThanOrEqual" or
                "Validation.InvalidCharacter" or "Validation.InvalidRange" or "Validation.InvalidValue" or
                "Validation.InvalidVersion" or "Validation.MaximumLength" or "Validation.PrecisionScale" or
                "Validation.Range" or "Validation.Required" => code,
            "SalesReceipt.AggregateOutOfRange" or "SalesReceipt.AlreadyPosted" or "SalesReceipt.AmountOutOfRange" or
                "SalesReceipt.ConcurrencyConflict" or "SalesReceipt.DuplicateLineIdentity" or
                "SalesReceipt.InsufficientStock" or "SalesReceipt.InvalidLineIdentity" or
                "SalesReceipt.InvalidLineOwnership" or "SalesReceipt.InvalidStoredAmount" or
                "SalesReceipt.InvalidStoredLines" or "SalesReceipt.InvalidUnit" or "SalesReceipt.NotFound" or
                "SalesReceipt.UnitChanged" or "SalesReceipt.ValuationOutOfRange" => code,
            "GoodsReceipt.AlreadyPosted" or "GoodsReceipt.ConcurrencyConflict" or "GoodsReceipt.DuplicateProduct" or
                "GoodsReceipt.Employee" or "GoodsReceipt.InvalidLine" or "GoodsReceipt.InvalidLineCount" or
                "GoodsReceipt.InvalidQuantity" or "GoodsReceipt.InvalidUnitCost" or "GoodsReceipt.NotFound" or
                "GoodsReceipt.Product" or "GoodsReceipt.ValuationOutOfRange" or "GoodsReceipt.Warehouse" => code,
            _ => RefreshErrorCode(code),
        };

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
