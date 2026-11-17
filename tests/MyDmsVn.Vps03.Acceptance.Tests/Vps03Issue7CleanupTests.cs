using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03Issue7CleanupTests
{
    [Vps03AcceptanceFact]
    [Trait("Category", "Vps03Issue7Cleanup")]
    [Trait("CaseId", "VPS03.ISSUE7.CLEANUP")]
    public void Runner_observed_every_owned_resource_absent()
    {
        var path = Environment.GetEnvironmentVariable("MYDMSVN_VPS03_CLEANUP_OBSERVATION");
        Assert.False(string.IsNullOrWhiteSpace(path));
        Assert.True(Path.IsPathRooted(path));
        Assert.True(File.Exists(path));
        var data = JObject.Parse(File.ReadAllText(path!));
        Assert.Equal(1, (int?)data["schemaVersion"]);
        Assert.Equal(Environment.GetEnvironmentVariable("MYDMSVN_VPS03_HEAD_SHA"),
            (string?)data["headSha"]);
        foreach (var name in new[] { "childAbsent", "iisAbsent", "sqlAbsent", "intentAbsent" })
            Assert.True((bool?)data[name] == true, name + " was not explicitly observed.");
        Assert.True((int?)data["environmentLedgers"] >= 1);
    }
}
