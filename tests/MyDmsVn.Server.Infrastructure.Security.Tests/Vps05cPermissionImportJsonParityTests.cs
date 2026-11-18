using MyDmsVn.Contracts.Security;
using MyDmsVn.Server.Infrastructure.Security;
using Xunit;

namespace MyDmsVn.Server.Infrastructure.Security.Tests;

public sealed class Vps05cPermissionImportJsonParityTests
{
    [Fact]
    public void Net48JsonNetPermissionImportSqlShapePreservesUnicodeOrderAndGrantedValues()
    {
        var entries = new[]
        {
            new PermissionAssignmentImportEntryDto("Báo cáo \"Đặc biệt\"\n", true),
            new PermissionAssignmentImportEntryDto("Catalog.Products.Read", false),
        };

        Assert.Equal("[{\"PermissionKey\":\"Báo cáo \\\"Đặc biệt\\\"\\n\",\"Granted\":true}," +
            "{\"PermissionKey\":\"Catalog.Products.Read\",\"Granted\":false}]",
            SqlSecurityAdministrationRepository.SerializePermissionImportEntries(entries));
    }
}
