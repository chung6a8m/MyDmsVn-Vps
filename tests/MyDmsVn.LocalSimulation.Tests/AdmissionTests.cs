using System;
using System.Collections.Generic;
using MyDmsVn.LocalSimulation;
using Xunit;

namespace MyDmsVn.LocalSimulation.Tests;

public sealed class AdmissionTests
{
    [Fact]
    public void MissingExtendedPropertyIsNotTreatedAsAnOwnershipMismatch()
    {
        Assert.Null(SqlLifecycle.NormalizeMarkerValue(null));
        Assert.Null(SqlLifecycle.NormalizeMarkerValue(DBNull.Value));
        Assert.Equal("instance", SqlLifecycle.NormalizeMarkerValue("instance"));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("backup-verify")]
    [InlineData("provision")]
    [InlineData("verify")]
    [InlineData("reset-admission")]
    public void OnlyBoundedLifecycleCommandsAreAdmitted(string command)
    {
        Assert.True(LocalSimulationCommands.IsAllowed(command));
        Assert.False(LocalSimulationCommands.IsAllowed("drop-database"));
    }

    [Fact]
    public void ExactSyntheticLocalSettingsAreAdmitted()
    {
        var settings = LocalSimulationSettings.Parse(Valid());

        Assert.Equal("MyDmsVnVps_LocalSimulation", settings.Database);
        Assert.Equal("127.0.0.1,11433", settings.AdminConnection.DataSource);
        Assert.Equal("mydmsvn_local_sim_runtime", LocalSimulationSqlNames.RuntimeLogin);
        Assert.Equal("mydmsvn_local_sim_oracle", LocalSimulationSqlNames.OracleLogin);
        Assert.NotEqual(LocalSimulationSqlNames.RuntimeLogin, LocalSimulationSqlNames.OracleLogin);
        Assert.DoesNotContain("Password", settings.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("unapproved", "LocalSimulation.AdmissionRefused")]
    [InlineData("", "LocalSimulation.AdmissionRefused")]
    public void MissingOrWrongAdmissionIsRefused(string admission, string expectedCode)
    {
        var input = Valid();
        input["MYDMSVN_LOCAL_SIM_ADMISSION"] = admission;

        var error = Assert.Throws<LocalSimulationBlockedException>(() =>
            LocalSimulationSettings.Parse(input));

        Assert.Equal(expectedCode, error.Code);
    }

    [Theory]
    [InlineData("Server=127.0.0.1,11433;Initial Catalog=master;User ID=sa;Password=Secret_1234567890!;Encrypt=False;TrustServerCertificate=False", "LocalSimulation.SqlTlsRequired")]
    [InlineData("Server=127.0.0.1,11433;Initial Catalog=master;User ID=sa;Password=Secret_1234567890!;Encrypt=True;TrustServerCertificate=True", "LocalSimulation.SqlTlsRequired")]
    [InlineData("Server=localhost,1433;Initial Catalog=master;User ID=sa;Password=Secret_1234567890!;Encrypt=True;TrustServerCertificate=False", "LocalSimulation.SqlEndpointRefused")]
    [InlineData("Server=127.0.0.1,11433;Initial Catalog=Production;User ID=sa;Password=Secret_1234567890!;Encrypt=True;TrustServerCertificate=False", "LocalSimulation.AdminDatabaseRefused")]
    public void UnsafeAdminConnectionIsRefused(string connection, string expectedCode)
    {
        var input = Valid();
        input["MYDMSVN_LOCAL_SIM_SQL_ADMIN_CONNECTION_STRING"] = connection;

        var error = Assert.Throws<LocalSimulationBlockedException>(() =>
            LocalSimulationSettings.Parse(input));

        Assert.Equal(expectedCode, error.Code);
        Assert.DoesNotContain("Secret_1234567890!", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeConnectionUsesDedicatedIdentityAndExactDatabase()
    {
        var settings = LocalSimulationSettings.Parse(Valid());

        var runtime = settings.BuildRuntimeConnectionString("host.docker.internal,11433");

        Assert.Equal("MyDmsVnVps_LocalSimulation", runtime.InitialCatalog);
        Assert.Equal(LocalSimulationSqlNames.RuntimeLogin, runtime.UserID);
        Assert.Equal("host.docker.internal,11433", runtime.DataSource);
        Assert.True(runtime.Encrypt == Microsoft.Data.SqlClient.SqlConnectionEncryptOption.Mandatory);
        Assert.False(runtime.TrustServerCertificate);
    }

    private static Dictionary<string, string> Valid() => new(StringComparer.Ordinal)
    {
        ["MYDMSVN_LOCAL_SIM_ADMISSION"] = "approved-synthetic-local-only",
        ["MYDMSVN_LOCAL_SIM_INSTANCE_ID"] = "11111111-2222-4333-8444-555555555555",
        ["MYDMSVN_LOCAL_SIM_SQL_ADMIN_CONNECTION_STRING"] = "Server=127.0.0.1,11433;Initial Catalog=master;User ID=sa;Password=Secret_1234567890!;Encrypt=True;TrustServerCertificate=False;Pooling=False",
        ["MYDMSVN_LOCAL_SIM_RUNTIME_PASSWORD"] = "Runtime_1234567890!Aa",
        ["MYDMSVN_LOCAL_SIM_ORACLE_PASSWORD"] = "Oracle_1234567890!Aa",
        ["MYDMSVN_LOCAL_SIM_WRITER_PASSWORD"] = "Writer_1234567890!Aa",
    };
}
