using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using MyDmsVn.Contracts;
using MyDmsVn.Contracts.Security;
using MyDmsVn.Server.Infrastructure.Security;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps05cTargetedRegressionTests
{
    [Fact]
    public void PermissionImportSqlJsonMatchesLegacyPropertyOrderUnicodeEscapingAndGrantedValues()
    {
        var serializer = typeof(SqlSecurityAdministrationRepository).GetMethod(
            "SerializePermissionImportEntries", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(serializer);
        var entries = new[]
        {
            new PermissionAssignmentImportEntryDto("Báo cáo \"Đặc biệt\"\n", true),
            new PermissionAssignmentImportEntryDto("Catalog.Products.Read", false),
        };

        var actual = Assert.IsType<string>(serializer!.Invoke(null, new object[] { entries }));
        Assert.Equal("[{\"PermissionKey\":\"Báo cáo \\\"Đặc biệt\\\"\\n\",\"Granted\":true}," +
            "{\"PermissionKey\":\"Catalog.Products.Read\",\"Granted\":false}]", actual);
    }

    [Fact]
    public void NullableSalesListCalendarDatesRoundTripDirectlyThroughSystemTextJson()
    {
        var request = new SalesReceiptListRequest(
            new DateTime(2026, 11, 18, 23, 59, 58, DateTimeKind.Utc),
            null, null, null, null, null, null, null);
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        var json = JsonSerializer.Serialize(request, options);
        Assert.Contains("\"fromDate\":\"2026-11-18\"", json, StringComparison.Ordinal);
        Assert.Contains("\"toDate\":null", json, StringComparison.Ordinal);

        var copy = JsonSerializer.Deserialize<SalesReceiptListRequest>(
            "{\"fromDate\":null,\"toDate\":\"2026-11-19\",\"pageSize\":50}", options);
        Assert.NotNull(copy);
        Assert.Null(copy!.FromDate);
        Assert.Equal(new DateTime(2026, 11, 19), copy.ToDate);
        Assert.Equal(DateTimeKind.Unspecified, copy.ToDate!.Value.Kind);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SalesReceiptListRequest>(
            "{\"fromDate\":\"2026-11-19T00:00:00Z\",\"pageSize\":50}", options));
    }
}
