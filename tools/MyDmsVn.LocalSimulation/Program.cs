using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace MyDmsVn.LocalSimulation;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 1 || !LocalSimulationCommands.IsAllowed(args[0]))
                throw new LocalSimulationBlockedException("LocalSimulation.CommandInvalid");

            var settings = LocalSimulationSettings.Parse(ReadEnvironment());
            var lifecycle = new SqlLifecycle(settings);
            await lifecycle.ExecuteAsync(args[0]).ConfigureAwait(false);
            Console.Out.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                command = args[0],
                status = "PASS",
                database = LocalSimulationSqlNames.Database,
                instanceId = settings.InstanceId,
                notice = "LOCAL SIMULATION - NOT VPS-08/09 ACCEPTANCE",
            }));
            return 0;
        }
        catch (LocalSimulationBlockedException error)
        {
            WriteFailure(error.Code);
            return 2;
        }
        catch (SqlException error)
        {
            WriteFailure("LocalSimulation.SqlError." + error.Number);
            return 1;
        }
        catch (Exception error)
        {
            WriteFailure("LocalSimulation.Unexpected." + error.GetType().Name);
            return 1;
        }
    }

    private static Dictionary<string, string> ReadEnvironment()
    {
        var names = new[]
        {
            "MYDMSVN_LOCAL_SIM_ADMISSION",
            "MYDMSVN_LOCAL_SIM_INSTANCE_ID",
            "MYDMSVN_LOCAL_SIM_SQL_ADMIN_CONNECTION_STRING",
            "MYDMSVN_LOCAL_SIM_RUNTIME_PASSWORD",
            "MYDMSVN_LOCAL_SIM_ORACLE_PASSWORD",
            "MYDMSVN_LOCAL_SIM_WRITER_PASSWORD",
        };
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names)
            values[name] = Environment.GetEnvironmentVariable(name) ?? string.Empty;
        return values;
    }

    private static void WriteFailure(string code) => Console.Out.WriteLine(JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        status = "REFUSED",
        code,
        notice = "LOCAL SIMULATION - NOT VPS-08/09 ACCEPTANCE",
    }));
}
