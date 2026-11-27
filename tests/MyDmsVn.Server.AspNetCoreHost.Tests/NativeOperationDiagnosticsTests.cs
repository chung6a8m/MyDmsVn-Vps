using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Contracts;
using MyDmsVn.Server.HttpHost;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class NativeOperationDiagnosticsTests
{
    [Fact]
    public async Task Native_error_records_status_and_code_without_recording_response_content()
    {
        var sink = new CaptureSink();
        using var logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        using var services = new ServiceCollection().AddSingleton<ILogger>(logger).BuildServiceProvider();
        var operationId = Guid.NewGuid();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Headers["Idempotency-Key"] = operationId.ToString("D");
        context.Response.Body = new MemoryStream();

        await NativeHttpErrors.WriteAsync(context,
            new ApiError(ApiStatusCode.BadRequest, "ValidationError", "private message",
                new[] { new ApiErrorDetail("SalesReceipt.InsufficientStock", "private detail", "Request.Lines") }));

        var entry = Assert.Single(sink.Events);
        Assert.Equal(operationId, Assert.IsType<ScalarValue>(entry.Properties["OperationId"]).Value);
        Assert.Equal(400, Assert.IsType<ScalarValue>(entry.Properties["HttpStatus"]).Value);
        Assert.Equal("ValidationError", Assert.IsType<ScalarValue>(entry.Properties["ErrorCode"]).Value);
        var details = Assert.IsType<SequenceValue>(entry.Properties["DetailCodes"]);
        Assert.Equal("SalesReceipt.InsufficientStock", Assert.IsType<ScalarValue>(Assert.Single(details.Elements)).Value);
        Assert.DoesNotContain("private message", entry.RenderMessage());
        Assert.DoesNotContain("private detail", entry.RenderMessage());
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = new();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
