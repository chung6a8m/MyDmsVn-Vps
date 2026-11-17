using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Desktop.Infrastructure.Http;
using MyDmsVn.Server.DbMigrator;
using MyDmsVn.Server.Infrastructure.Identity;
using MyDmsVn.Server.Infrastructure.IntegrationTests;
using MyDmsVn.Server.Infrastructure.Operations;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03OwnedEnvironment
{
    private readonly Vps03AcceptanceOptions _options;
    private readonly Vps03IisExpressHost _host;
    private readonly ServiceProvider _desktopServices;
    private readonly string _databaseName;
    private readonly string _loginName;
    private readonly byte[] _loginSid;
    private readonly string _diagnosticDirectory;
    private bool _disposed;

    private Vps03OwnedEnvironment(Vps03AcceptanceOptions options, Vps03PreflightResult preflight,
        int schema, string runtimeConnectionString, string databaseName, string loginName,
        byte[] loginSid, string diagnosticDirectory, Vps03IisExpressHost host,
        ServiceProvider desktopServices, string username, string password)
    {
        _options = options;
        _host = host;
        _desktopServices = desktopServices;
        _databaseName = databaseName;
        _loginName = loginName;
        _loginSid = loginSid;
        _diagnosticDirectory = diagnosticDirectory;
        Origin = preflight.Origin;
        Mode = ModeForSchema(schema);
        OracleConnectionString = DatabaseConnection(options.SqlConnectionString, databaseName);
        RuntimeConnectionString = runtimeConnectionString;
        DesktopExecutable = Path.Combine(options.DesktopPackage, "MyDmsVn.Desktop.App.exe");
        HeadSha = preflight.HeadSha;
        Username = username;
        Password = password;
    }

    internal Uri Origin { get; }
    internal string Mode { get; }
    internal IServiceProvider DesktopServices => _desktopServices;
    internal string OracleConnectionString { get; }
    internal string RuntimeConnectionString { get; }
    internal string DesktopExecutable { get; }
    internal string HeadSha { get; }
    internal string Username { get; }
    internal string Password { get; }

    internal ServiceProvider CreateDesktopServices() => new ServiceCollection()
        .AddHttpDesktopAdapter(new HttpDesktopOptions(Origin.GetLeftPart(UriPartial.Authority)),
            new HttpDesktopOperationSettings(OperationMode(13)))
        .BuildServiceProvider();

    internal static async Task<Vps03OwnedEnvironment> CreateAsync(int schema, CancellationToken token)
    {
        var mode = ModeForSchema(schema);
        var options = Vps03AcceptanceOptions.FromEnvironment(Environment.GetEnvironmentVariable);
        var preflight = Vps03AcceptancePreflight.Validate(options);
        var databaseName = "MyDmsVnVps03_" + Guid.NewGuid().ToString("N");
        var loginName = "Vps03Web_" + Guid.NewGuid().ToString("N");
        var loginSid = Guid.NewGuid().ToByteArray();
        var loginPassword = "Vps03!" + Guid.NewGuid().ToString("N");
        var username = "vps03.owner";
        var password = "Vps03Owner!" + Guid.NewGuid().ToString("N");
        var diagnosticDirectory = Path.Combine(options.ResultsDirectory,
            "schema-" + schema + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(diagnosticDirectory);
        Vps03IisExpressHost? host = null;
        ServiceProvider? services = null;
        var databaseCreated = false;
        var loginCreated = false;
        try
        {
            await CreateDatabaseAsync(options.SqlConnectionString, databaseName, token).ConfigureAwait(false);
            databaseCreated = true;
            var adminDatabase = DatabaseConnection(options.SqlConnectionString, databaseName);
            var migration = schema switch
            {
                11 => FrozenWeb11Migration.Install(adminDatabase),
                12 => new DatabaseMigrationRunner().MigrateWeb(adminDatabase),
                13 => new DatabaseMigrationRunner().MigrateWeb5(adminDatabase),
                _ => throw new ArgumentOutOfRangeException(nameof(schema)),
            };
            if (!migration.Successful) throw new InvalidOperationException("VPS-03 schema migration failed.", migration.Error);
            await CreateLoginAsync(options.SqlConnectionString, loginName, loginPassword, loginSid, token).ConfigureAwait(false);
            loginCreated = true;
            var runtimeConnection = RuntimeConnection(options.SqlConnectionString, databaseName, loginName, loginPassword);
            await ProvisionAsync(adminDatabase, loginName, username, password, token).ConfigureAwait(false);
            await new SqlWebRuntimePrincipalValidator(requireJournal: schema >= 12,
                requireResolution: schema >= 13, requireDesktopApi: true)
                .ValidateAsync(runtimeConnection, token).ConfigureAwait(false);
            var principal = await new Vps03SqlOracle(runtimeConnection).RuntimePrincipalAsync(token).ConfigureAwait(false);
            Vps03SqlOracle.ValidateRuntimePrincipal(principal);
            var compatibility = schema == 13
                ? await new SqlWeb5SchemaCompatibilityChecker(runtimeConnection).CheckAsync(token).ConfigureAwait(false)
                : schema == 12
                    ? await new SqlWebSchemaCompatibilityChecker(runtimeConnection).CheckAsync(token).ConfigureAwait(false)
                    : await new SqlWebSchema11CompatibilityChecker(runtimeConnection).CheckAsync(token).ConfigureAwait(false);
            if (!compatibility.IsCompatible) throw new InvalidOperationException("VPS-03 runtime schema compatibility failed.");
            await new SqlSuperuserBindingValidator().ValidateAsync(runtimeConnection, username, token).ConfigureAwait(false);

            var signingKey = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(signingKey);
            var hostEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["MYDMSVN_WEB_ORIGIN"] = preflight.Origin.GetLeftPart(UriPartial.Authority),
                ["MYDMSVN_WEB_JWT_ISSUER"] = preflight.Origin.GetLeftPart(UriPartial.Authority),
                ["MYDMSVN_WEB_JWT_AUDIENCE"] = "mydmsvn-vps03-acceptance",
                ["MYDMSVN_WEB_JWT_ACTIVE_KID"] = "vps03",
                ["MYDMSVN_WEB_JWT_KEYS"] = "vps03:" + Convert.ToBase64String(signingKey),
                ["MYDMSVN_WEB_ACCESS_LIFETIME_MINUTES"] = "1",
                ["MYDMSVN_WEB_REFRESH_IDLE_DAYS"] = "1",
                ["MYDMSVN_WEB_REFRESH_ABSOLUTE_DAYS"] = "1",
                ["MYDMSVN_WEB_SQL_CONNECTION_STRING"] = runtimeConnection,
                ["MYDMSVN_WEB_SUPERUSER_USERNAME"] = username,
                ["MYDMSVN_WEB_OPERATION_MODE"] = mode,
                ["MYDMSVN_WEB_DESKTOP_API"] = "enabled",
            };
            host = await Vps03IisExpressHost.StartAsync(preflight.Origin, preflight.TlsThumbprint,
                options.HostPackage, hostEnvironment, token).ConfigureAwait(false);
            services = new ServiceCollection()
                .AddHttpDesktopAdapter(new HttpDesktopOptions(preflight.Origin.GetLeftPart(UriPartial.Authority)),
                    new HttpDesktopOperationSettings(OperationMode(schema)))
                .BuildServiceProvider();
            return new Vps03OwnedEnvironment(options, preflight, schema, runtimeConnection,
                databaseName, loginName, loginSid, diagnosticDirectory, host, services, username, password);
        }
        catch
        {
            services?.Dispose();
            if (host != null)
            {
                try { await host.StopAndVerifyAbsentAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
            SqlConnection.ClearAllPools();
            try
            {
                await CleanupSqlAsync(options.SqlConnectionString, databaseName, databaseCreated,
                    loginName, loginSid, loginCreated, CancellationToken.None).ConfigureAwait(false);
            }
            catch { }
            throw;
        }
    }

    internal async Task DisposeAsync()
    {
        if (_disposed) return;
        var failures = new List<Exception>();
        try { await _host.StopAndVerifyAbsentAsync(CancellationToken.None).ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        _desktopServices.Dispose();
        SqlConnection.ClearAllPools();
        try
        {
            await CleanupSqlAsync(_options.SqlConnectionString, _databaseName, databaseCreated: true,
                _loginName, _loginSid, loginCreated: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) { failures.Add(error); }
        _disposed = failures.Count == 0;
        var cleanup = Vps03OwnedCleanup.Complete(_disposed, _disposed, _diagnosticDirectory);
        if (!cleanup.Verified) throw new AggregateException("VPS-03 owned cleanup was not verified; diagnostics retained.", failures);
    }

    internal static string ModeForSchema(int schema) => schema switch
    {
        11 => "legacy-web11",
        12 => "web4",
        13 => "web5",
        _ => throw new ArgumentOutOfRangeException(nameof(schema)),
    };

    internal static IReadOnlyDictionary<string, string?> DesktopChildEnvironment(
        IReadOnlyDictionary<string, string?> source, Uri origin, string mode)
    {
        var result = source.Where(item => !IsSensitiveChildKey(item.Key))
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        result["MYDMSVN_DESKTOP_API_ORIGIN"] = origin.GetLeftPart(UriPartial.Authority);
        result["MYDMSVN_DESKTOP_OPERATION_MODE"] = mode;
        return result;
    }

    private static HttpDesktopOperationMode OperationMode(int schema) => schema switch
    {
        11 => HttpDesktopOperationMode.LegacyWeb11,
        12 => HttpDesktopOperationMode.Web4,
        13 => HttpDesktopOperationMode.Web5,
        _ => throw new ArgumentOutOfRangeException(nameof(schema)),
    };

    private static async Task CreateDatabaseAsync(string admin, string database, CancellationToken token)
    {
        using var connection = new SqlConnection(MasterConnection(admin));
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE DATABASE " + QuoteIdentifier(database) + ";";
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task CreateLoginAsync(string admin, string login, string password,
        byte[] sid, CancellationToken token)
    {
        using var connection = new SqlConnection(MasterConnection(admin));
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE LOGIN " + QuoteIdentifier(login) + " WITH PASSWORD=N'" +
            password.Replace("'", "''") + "', SID=0x" + BitConverter.ToString(sid).Replace("-", string.Empty) +
            ", CHECK_POLICY=OFF;";
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task ProvisionAsync(string adminDatabase, string login,
        string username, string password, CancellationToken token)
    {
        using var connection = new SqlConnection(adminDatabase);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE USER " + QuoteIdentifier(login) + " FOR LOGIN " + QuoteIdentifier(login) +
            "; CREATE USER [Vps03Migration] WITHOUT LOGIN;";
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        var root = RepositoryRoot();
        command.CommandText = File.ReadAllText(Path.Combine(root, "deployment", "sql", "Provision-MyDmsVnWebPrincipals.sql"))
            .Replace("$(WebRuntimeUser)", login).Replace("$(MigrationUser)", "Vps03Migration");
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        command.CommandText = File.ReadAllText(Path.Combine(root, "deployment", "sql", "Provision-MyDmsVnDesktopApiGrants.sql"))
            .Replace("$(WebRuntimeUser)", login);
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        var hash = new BcryptPasswordHasher().Hash(password);
        command.Parameters.Clear();
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@normalized", username.ToUpperInvariant());
        command.Parameters.AddWithValue("@hash", hash);
        command.CommandText = @"INSERT dbo.Users(Username,NormalizedUsername,DisplayName,Source,PasswordHash,PasswordSalt,PasswordAlgorithm)
VALUES(@username,@normalized,N'VPS-03 Owner',N'Local',@hash,N'',N'BCrypt');
INSERT dbo.SystemSecuritySettings(Id,SuperuserUserId,BoundNormalizedUsername)
SELECT 1,UserId,@normalized FROM dbo.Users WHERE NormalizedUsername=@normalized;";
        await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    private static async Task CleanupSqlAsync(string admin, string database, bool databaseCreated,
        string login, byte[] sid, bool loginCreated, CancellationToken token)
    {
        if (!database.StartsWith("MyDmsVnVps03_", StringComparison.Ordinal) ||
            !login.StartsWith("Vps03Web_", StringComparison.Ordinal))
            throw new InvalidDataException("Refusing cleanup outside owned VPS-03 SQL names.");
        using var connection = new SqlConnection(MasterConnection(admin));
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        if (databaseCreated)
        {
            command.Parameters.AddWithValue("@database", database);
            command.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name=@database;";
            if ((int)await command.ExecuteScalarAsync(token).ConfigureAwait(false) == 1)
            {
                command.Parameters.Clear();
                command.CommandText = "ALTER DATABASE " + QuoteIdentifier(database) +
                    " SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE " + QuoteIdentifier(database) + ";";
                await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }
        if (loginCreated)
        {
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@login", login);
            command.Parameters.AddWithValue("@sid", sid);
            command.CommandText = "IF EXISTS(SELECT 1 FROM sys.server_principals WHERE name=@login AND sid<>@sid) " +
                "THROW 51998,'VPS-03 owned login identity changed.',1; IF EXISTS(SELECT 1 FROM sys.server_principals WHERE name=@login) " +
                "DROP LOGIN " + QuoteIdentifier(login) + ";";
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
    }

    private static string MasterConnection(string source) => new SqlConnectionStringBuilder(source)
    {
        InitialCatalog = "master",
        Pooling = false,
    }.ConnectionString;

    private static string DatabaseConnection(string source, string database) => new SqlConnectionStringBuilder(source)
    {
        InitialCatalog = database,
        Pooling = false,
    }.ConnectionString;

    private static string RuntimeConnection(string source, string database, string login, string password) =>
        new SqlConnectionStringBuilder(source)
        {
            InitialCatalog = database,
            IntegratedSecurity = false,
            Authentication = SqlAuthenticationMethod.NotSpecified,
            UserID = login,
            Password = password,
            Pooling = false,
        }.ConnectionString;

    private static string QuoteIdentifier(string value) => "[" + value.Replace("]", "]]" ) + "]";

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "MyDmsVn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static bool IsSensitiveChildKey(string key) =>
        key.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0 ||
        key.IndexOf("PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0 ||
        key.IndexOf("JWT", StringComparison.OrdinalIgnoreCase) >= 0 ||
        key.IndexOf("TOKEN", StringComparison.OrdinalIgnoreCase) >= 0 ||
        key.IndexOf("SECRET", StringComparison.OrdinalIgnoreCase) >= 0;

}

internal sealed class Vps03CleanupResult
{
    internal Vps03CleanupResult(bool verified, string state)
    {
        Verified = verified;
        State = state;
    }

    internal bool Verified { get; }
    internal string State { get; }
}

internal static class Vps03OwnedCleanup
{
    internal static Vps03CleanupResult Complete(bool processAbsent, bool sqlAbsent, string diagnosticDirectory)
    {
        if (!processAbsent || !sqlAbsent)
            return new Vps03CleanupResult(false, "failed");
        if (Directory.Exists(diagnosticDirectory)) Directory.Delete(diagnosticDirectory, true);
        return new Vps03CleanupResult(true, "verified");
    }
}
