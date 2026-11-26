using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace MyDmsVn.Server.HttpHost;

internal sealed class NativeOperationDiagnostics : IAlwaysRunResultFilter
{
    private static readonly Regex ErrorCodePattern = new("\\A[A-Za-z][A-Za-z0-9_.]{0,95}\\z");

    internal static void RecordError(HttpContext context, ApiError error)
    {
        var key = context.Request.Headers["Idempotency-Key"];
        if (key.Count != 1 || !Guid.TryParseExact(key[0], "D", out var operationId) || operationId == Guid.Empty)
            return;

        var code = ErrorCodePattern.IsMatch(error.Code) ? error.Code : "UnrecognizedErrorCode";
        var details = error.Details.Take(8).Select(detail => detail.Code)
            .Where(value => ErrorCodePattern.IsMatch(value)).Distinct().ToArray();
        context.RequestServices?.GetService<ILogger>()?.Information(
            "Operation HTTP error {OperationId} {HttpStatus} {ErrorCode} {DetailCodes}",
            operationId, (int)error.Status, code, details);
    }

    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.Result is JsonResult { Value: V1ErrorEnvelope envelope })
            RecordError(context.HttpContext, envelope.Error);
    }

    public void OnResultExecuted(ResultExecutedContext context) { }

    internal sealed class JournalSink : ILogEventSink
    {
        private readonly JsonFormatter _formatter = new();

        public void Emit(LogEvent logEvent)
        {
            using var buffer = new System.IO.StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            _formatter.Format(logEvent, buffer);
            Console.Error.Write(buffer.ToString());
        }
    }
}
