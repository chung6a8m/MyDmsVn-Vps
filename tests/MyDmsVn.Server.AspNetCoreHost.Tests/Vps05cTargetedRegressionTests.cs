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
    public void NullableSalesListCalendarConverterRoundTripsValueNullAndRejectsTimestamp()
    {
        var options = new JsonSerializerOptions
        {
            Converters = { new SalesCalendarDateJsonConverter() },
        };

        DateTime? fromDate = new DateTime(2026, 11, 18, 23, 59, 58, DateTimeKind.Utc);
        Assert.Equal("\"2026-11-18\"", JsonSerializer.Serialize(fromDate, options));
        Assert.Equal("null", JsonSerializer.Serialize<DateTime?>(null, options));
        var toDate = JsonSerializer.Deserialize<DateTime?>("\"2026-11-19\"", options);
        Assert.Equal(new DateTime(2026, 11, 19), toDate);
        Assert.Equal(DateTimeKind.Unspecified, toDate!.Value.Kind);
        Assert.Null(JsonSerializer.Deserialize<DateTime?>("null", options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime?>(
            "\"2026-11-19T00:00:00Z\"", options));
    }
}
