using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace MyDmsVn.LocalSimulation;

public static class LocalSimulationSqlNames
{
    public const string Database = "MyDmsVnVps_LocalSimulation";
    public const string RuntimeLogin = "mydmsvn_local_sim_runtime";
    public const string OracleLogin = "mydmsvn_local_sim_oracle";
    public const string WriterUsername = "local_sim_writer";
    public const string DeniedUsername = "local_sim_denied";
}

public sealed class LocalSimulationBlockedException : Exception
{
    public LocalSimulationBlockedException(string code) : base(code) => Code = code;

    public string Code { get; }
}

public sealed class LocalSimulationSettings
{
    private static readonly Regex PasswordPolicy = new(
        @"\A[A-Za-z0-9_@%!\-]{20,80}\z", RegexOptions.CultureInvariant);

    private LocalSimulationSettings(
        Guid instanceId,
        SqlConnectionStringBuilder adminConnection,
        string runtimePassword,
        string oraclePassword,
        string writerPassword)
    {
        InstanceId = instanceId;
        AdminConnection = adminConnection;
        RuntimePassword = runtimePassword;
        OraclePassword = oraclePassword;
        WriterPassword = writerPassword;
    }

    public Guid InstanceId { get; }
    public string Database => LocalSimulationSqlNames.Database;
    public SqlConnectionStringBuilder AdminConnection { get; }
    internal string RuntimePassword { get; }
    internal string OraclePassword { get; }
    internal string WriterPassword { get; }

    public static LocalSimulationSettings Parse(IReadOnlyDictionary<string, string> values)
    {
        if (!values.TryGetValue("MYDMSVN_LOCAL_SIM_ADMISSION", out var admission) ||
            admission != "approved-synthetic-local-only")
            throw new LocalSimulationBlockedException("LocalSimulation.AdmissionRefused");

        if (!values.TryGetValue("MYDMSVN_LOCAL_SIM_INSTANCE_ID", out var instanceText) ||
            !Guid.TryParseExact(instanceText, "D", out var instanceId))
            throw new LocalSimulationBlockedException("LocalSimulation.InstanceIdInvalid");

        var connection = Require(values, "MYDMSVN_LOCAL_SIM_SQL_ADMIN_CONNECTION_STRING");
        SqlConnectionStringBuilder admin;
        try { admin = new SqlConnectionStringBuilder(connection); }
        catch (ArgumentException) { throw new LocalSimulationBlockedException("LocalSimulation.AdminConnectionInvalid"); }

        if (!string.Equals(admin.DataSource, "127.0.0.1,11433", StringComparison.OrdinalIgnoreCase))
            throw new LocalSimulationBlockedException("LocalSimulation.SqlEndpointRefused");
        if (!string.Equals(admin.InitialCatalog, "master", StringComparison.OrdinalIgnoreCase))
            throw new LocalSimulationBlockedException("LocalSimulation.AdminDatabaseRefused");
        if (admin.IntegratedSecurity || admin.TrustServerCertificate ||
            admin.Encrypt != SqlConnectionEncryptOption.Mandatory)
            throw new LocalSimulationBlockedException("LocalSimulation.SqlTlsRequired");
        if (!string.Equals(admin.UserID, "sa", StringComparison.OrdinalIgnoreCase))
            throw new LocalSimulationBlockedException("LocalSimulation.AdminIdentityRefused");

        var runtime = Password(values, "MYDMSVN_LOCAL_SIM_RUNTIME_PASSWORD");
        var oracle = Password(values, "MYDMSVN_LOCAL_SIM_ORACLE_PASSWORD");
        var writer = Password(values, "MYDMSVN_LOCAL_SIM_WRITER_PASSWORD");
        if (runtime == oracle || runtime == writer || oracle == writer)
            throw new LocalSimulationBlockedException("LocalSimulation.PasswordReuseRefused");

        admin.Pooling = false;
        admin.ConnectTimeout = Math.Min(admin.ConnectTimeout, 8);
        return new LocalSimulationSettings(instanceId, admin, runtime, oracle, writer);
    }

    public SqlConnectionStringBuilder BuildRuntimeConnectionString(string dataSource) => new(AdminConnection.ConnectionString)
    {
        DataSource = dataSource,
        InitialCatalog = Database,
        UserID = LocalSimulationSqlNames.RuntimeLogin,
        Password = RuntimePassword,
        Encrypt = SqlConnectionEncryptOption.Mandatory,
        TrustServerCertificate = false,
        Pooling = false,
    };

    public SqlConnectionStringBuilder BuildOracleConnectionString(string dataSource) => new(AdminConnection.ConnectionString)
    {
        DataSource = dataSource,
        InitialCatalog = Database,
        UserID = LocalSimulationSqlNames.OracleLogin,
        Password = OraclePassword,
        Encrypt = SqlConnectionEncryptOption.Mandatory,
        TrustServerCertificate = false,
        Pooling = false,
    };

    public override string ToString() =>
        $"LocalSimulationSettings {{ InstanceId = {InstanceId:D}, Database = {Database}, Endpoint = 127.0.0.1,11433 }}";

    private static string Require(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new LocalSimulationBlockedException("LocalSimulation.SettingMissing");

    private static string Password(IReadOnlyDictionary<string, string> values, string name)
    {
        var value = Require(values, name);
        if (!PasswordPolicy.IsMatch(value) ||
            !Regex.IsMatch(value, "[A-Z]", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(value, "[a-z]", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(value, "[0-9]", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(value, "[^A-Za-z0-9]", RegexOptions.CultureInvariant))
            throw new LocalSimulationBlockedException("LocalSimulation.PasswordPolicyRefused");
        return value;
    }
}
