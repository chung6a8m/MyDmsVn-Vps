using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace MyDmsVn.Server.Infrastructure.IntegrationTests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SqlServerTheoryAttribute : TheoryAttribute, Xunit.v3.ITraitAttribute
{
    public SqlServerTheoryAttribute(params string[] categories)
    {
        Categories = categories is { Length: > 0 } ? categories : new[] { SqlTestCategory.FastSql };
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlTestDatabase.ConnectionStringEnvironmentVariable)))
        {
            Skip = "Set " + SqlTestDatabase.ConnectionStringEnvironmentVariable +
                " to a designated non-production master connection before SQL Theory execution.";
        }
    }
    public IReadOnlyList<string> Categories { get; }

    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        Categories.Select(category => new KeyValuePair<string, string>("Category", category)).ToArray();
}
