using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace MyDmsVn.Server.Infrastructure.IntegrationTests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SqlServerFactAttribute : FactAttribute, Xunit.v3.ITraitAttribute
{
    public SqlServerFactAttribute(params string[] categories)
    {
        Categories = categories is { Length: > 0 }
            ? categories
            : new[] { SqlTestCategory.FastSql };
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                SqlTestDatabase.ConnectionStringEnvironmentVariable)))
        {
            Skip =
                $"Set {SqlTestDatabase.ConnectionStringEnvironmentVariable} to a master-database connection string " +
                "for a designated non-production SQL Server test instance.";
        }
    }

    public IReadOnlyList<string> Categories { get; }

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        Categories.Select(category => new KeyValuePair<string, string>("Category", category)).ToArray();
}

internal static class SqlTestCategory
{
    public const string FastSql = "FastSQL";
    public const string MigrationSchema = "MigrationSchema";
    public const string SecurityProvisioning = "SecurityProvisioning";
    public const string SqlConcurrency = "SqlConcurrency";
}
