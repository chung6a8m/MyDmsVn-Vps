using System;
using System.Linq;
using System.Reflection;
using FluentMigrator;
using MyDmsVn.Server.FluentMigrator;

var names = new[]
{
    "001_PersistenceFoundation.sql",
    "002_Identity.sql",
    "003_Catalog.sql",
    "004_Catalog_Nul_Constraints.sql",
    "005_GoodsReceipt.sql",
    "006_GoodsReceipt_Number_Sequence.sql",
    "007_Inventory_Stock.sql",
    "008_Security_Administration.sql",
    "009_Security_Permission_Import.sql",
    "010_DirectSalesAndValuation.sql",
    "011_WebAuthSessions.sql",
    "012_WebOperationJournal.sql",
    "013_WebOperationTerminalResolution.sql",
};
var expectedBatches = new[] { 1, 1, 1, 1, 1, 1, 1, 2, 3, 3, 2, 3, 9 };
var migrations = typeof(ImmutableSqlMigration).Assembly.GetTypes()
    .Where(type => type.IsClass && !type.IsAbstract &&
                   typeof(ImmutableSqlMigration).IsAssignableFrom(type))
    .Select(type => new { Type = type, Attribute = type.GetCustomAttribute<MigrationAttribute>() })
    .OrderBy(item => item.Attribute?.Version)
    .ToArray();
if (migrations.Length != names.Length)
    throw new InvalidOperationException("VPS07A_EXPECTED_13_VERSIONED_MIGRATIONS");

var totalBatches = 0;
for (var index = 0; index < names.Length; index++)
{
    var entry = migrations[index];
    var instance = (ImmutableSqlMigration)(Activator.CreateInstance(entry.Type)
        ?? throw new InvalidOperationException("Missing migration constructor."));
    var scriptName = (string?)entry.Type
        .GetProperty("ScriptName", BindingFlags.NonPublic | BindingFlags.Instance)
        ?.GetValue(instance);
    if (entry.Attribute?.Version != index + 1 || scriptName != names[index])
        throw new InvalidOperationException("VPS07A_MIGRATION_VERSION_MAPPING_MISMATCH");

    var sql = ImmutableSqlScript.Read(names[index]);
    if (sql.Length == 0 || sql[0] == '\uFEFF')
        throw new InvalidOperationException("VPS07A_SQL_RESOURCE_BOM_OR_EMPTY");
    var batches = ImmutableSqlScript.Batches(names[index]);
    if (batches.Count != expectedBatches[index] || batches.Any(string.IsNullOrWhiteSpace))
        throw new InvalidOperationException("VPS07A_SQL_BATCH_COUNT_MISMATCH");
    try
    {
        instance.Down();
        throw new InvalidOperationException("VPS07A_DOWN_MUST_REFUSE");
    }
    catch (NotSupportedException) { }
    totalBatches += batches.Count;
}
if (totalBatches != 29)
    throw new InvalidOperationException("VPS07A_EXPECTED_29_SQL_BATCHES");
Console.WriteLine($"VPS07A_CONTRACT_PASS: versions={migrations.Length}; batches={totalBatches}; Down blocked={migrations.Length}");
