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

        try
        {
            var secret = Environment.GetEnvironmentVariable("VPS07B_SQL_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(secret))
                throw new Vps07bRefusal("Migration.ConnectionRequired");
            var target = int.Parse(args[1].Substring(6), CultureInfo.InvariantCulture);
            using var session = new Vps07bGuardedSession(secret, target);
            var admitted = session.Admit();
            session.Bridge(admitted);
            using (Vps07bExecutionScope.Enter(admitted.IsFresh))
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
        catch (Exception)
        {
            // Intentionally suppress SqlException text (contains SQL / login / identifiers).
            Console.Error.WriteLine("Migration.Failed: SQL transaction, journal and fingerprint must be reconciled before retry.");
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

