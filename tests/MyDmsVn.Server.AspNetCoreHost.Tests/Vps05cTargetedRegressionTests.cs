using System;
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
    public void SalesListDtoPreservesNullableCalendarAndPageSizeConstructorSemanticsUnderStj()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        var request = JsonSerializer.Deserialize<SalesReceiptListRequest>(
            "{\"fromDate\":null,\"toDate\":\"2026-11-19\",\"pageSize\":null}", options);
        Assert.NotNull(request);
        Assert.Null(request!.FromDate);
        Assert.Equal(new DateTime(2026, 11, 19), request.ToDate);
        Assert.Equal(DateTimeKind.Unspecified, request.ToDate!.Value.Kind);
        Assert.Equal(50, request.PageSize);
        Assert.Equal(50, JsonSerializer.Deserialize<SalesReceiptListRequest>(
            "{\"fromDate\":null,\"toDate\":null}", options)!.PageSize);

        var explicitPageSize = JsonSerializer.Deserialize<SalesReceiptListRequest>(
            "{\"fromDate\":\"2026-11-18\",\"toDate\":null,\"pageSize\":25}", options);
        Assert.Equal(25, explicitPageSize!.PageSize);
        Assert.Equal("2026-11-18", explicitPageSize.FromDate!.Value.ToString("yyyy-MM-dd"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SalesReceiptListRequest>(
            "{\"fromDate\":\"2026-11-19T00:00:00Z\",\"pageSize\":50}", options));
    }
}
