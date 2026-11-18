using System;
using System.Globalization;
using FluentMigrator.Runner;
using FluentMigrator.Runner.VersionTableInfo;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.FluentMigrator;

namespace MyDmsVn.Server.FluentMigrator.Cli;

internal static class Program
{
    internal static int Main(string[] args)
    {
        // Never accept a connection string in the process arguments, and never
        // infer a target from whatever migrations happen to be compiled.
        if (args.Length != 2 || args[0] != "migrate" ||
            (args[1] != "schema10" && args[1] != "schema12" && args[1] != "schema13"))
        {
            Console.Error.WriteLine("Migration.InvalidArguments: usage: migrate schema10|schema12|schema13.");
            return 2;
        }
        if (Environment.GetEnvironmentVariable("VPS07B_OPERATOR_APPROVAL") !=
            "approved-maintenance-and-verified-backup")
        {
            Console.Error.WriteLine("Migration.OperatorApprovalRequired: no database changes attempted.");
            return 2;
        }

        var stage = "configuration";
        try
        {
            var secret = Environment.GetEnvironmentVariable("VPS07B_SQL_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(secret))
                throw new Vps07bRefusal("Migration.ConnectionRequired");
            var target = int.Parse(args[1].Substring(6), CultureInfo.InvariantCulture);
            var faultVersion = 0;
            var faultBatch = 0;
            var fault = Environment.GetEnvironmentVariable("VPS07B_CI_FAULT");
            if (!string.IsNullOrEmpty(fault))
            {
                // Fault injection is NEVER admitted against any production,
                // staging or operator-supplied database. Fail closed.
                var ciConnection = new SqlConnectionStringBuilder(secret);
                if (Environment.GetEnvironmentVariable("CI") != "true" ||
                    Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
                    Environment.GetEnvironmentVariable("VPS07B_CI_EPHEMERAL_SQL") != "approved-container-only" ||
                    !string.Equals(ciConnection.DataSource, "localhost,1433", StringComparison.OrdinalIgnoreCase) ||
                    !ciConnection.InitialCatalog.StartsWith("MyDmsVnVps07bCi", StringComparison.Ordinal) ||
                    ciConnection.TrustServerCertificate || ciConnection.Encrypt.ToString() != "True")
                    throw new Vps07bRefusal("Migration.CiFaultScopeRefused");
                var tokens = fault.Split(':');
                if (tokens.Length != 2 ||
                    !int.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out faultVersion) ||
                    !int.TryParse(tokens[1], NumberStyles.None, CultureInfo.InvariantCulture, out faultBatch) ||
                    faultVersion < 1 || faultVersion > 13 || faultBatch < 1 || faultBatch > 9)
                    throw new Vps07bRefusal("Migration.InvalidCiFault");
            }
            using var session = new Vps07bGuardedSession(secret, target);
            stage = "preflight";
            var admitted = session.Admit();
            stage = "bridge";
            session.Bridge(admitted);
            stage = "fluent-migrator-up";
            using (Vps07bExecutionScope.Enter(admitted.IsFresh, faultVersion, faultBatch))
            {
                var services = new ServiceCollection()
                    .AddFluentMigratorCore()
                    .ConfigureRunner(builder => builder
                        .AddSqlServer()
                        .WithGlobalConnectionString(session.RunnerConnectionString)
                        .ScanIn(typeof(Historical001).Assembly).For.Migrations()
                        .WithVersionTable(new Vps07bVersionTable()));
                using var provider = services.BuildServiceProvider(validateScopes: true);
                using var scope = provider.CreateScope();
                scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp(target);
            }
            stage = "post-migrate-native-proof";
            session.VerifyAfterRun(target);
            Console.WriteLine("VPS07B_SQL_VERIFIED: target=schema" + target +
                "; source=schema" + admitted.Version + "; 2 journals reconciled.");
            return 0;
        }
        catch (Vps07bRefusal ex)
        {
            Console.Error.WriteLine(ex.Code + ": refused without unreviewed database mutations.");
            return 4;
        }
        catch (MyDmsVn.Server.DbMigrator.DatabaseMigrationPreflightException ex)
        {
            Console.Error.WriteLine(ex.Code + ": restricted maintenance admission refused.");
            return 4;
        }
        catch (Exception ex)
        {
            // Intentionally suppress exception text: SqlClient/FM may embed SQL or
            // connection credentials. Only fixed stage, type and error numbers.
            var underlying = ex;
            for (var depth = 0; depth < 6 && underlying.InnerException != null; depth++)
                underlying = underlying.InnerException;
            var number = underlying is SqlException sql ? sql.Number.ToString(CultureInfo.InvariantCulture) : "none";
            Console.Error.WriteLine("Migration.Failed: stage=" + stage + "; type=" +
                underlying.GetType().Name + "; sql=" + number + "; no acceptance.");
            return 1;
        }
    }
}

internal sealed class Vps07bVersionTable : IVersionTableMetaData
{
    public bool OwnsSchema => false;
    public string SchemaName => "dbo";
    public string TableName => "FluentMigratorVersionInfo";
    public string ColumnName => "Version";
    public string DescriptionColumnName => "Description";
    public string AppliedOnColumnName => "AppliedOn";
    public string UniqueIndexName => "UC_FluentMigratorVersion";
    public bool CreateWithPrimaryKey => false;
}

internal sealed class Vps07bRefusal : Exception
{
    internal Vps07bRefusal(string code) : base(code) { Code = code; }
    public string Code { get; }
}

