using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace MyDmsVn.Architecture.Tests;

public sealed class OperationalPersistenceBoundaryTests
{
    [Fact]
    public void Identity_security_operations_and_migrator_have_no_legacy_orm_consumers()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "MyDmsVn.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var paths = new[] { "MyDmsVn.Server.Infrastructure/Identity", "MyDmsVn.Server.Infrastructure/Security", "MyDmsVn.Server.Infrastructure/Operations", "MyDmsVn.Server.DbMigrator" };
        var files = paths.SelectMany(path => Directory.GetFiles(Path.Combine(root!.FullName, "src", path), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));
        Assert.Empty(files.Where(path => Regex.IsMatch(File.ReadAllText(path), @"\b(Dapper|RepoDb)\b")).Select(Path.GetFileName));
    }

    [Fact]
    public void Active_projects_and_locks_have_no_legacy_orm_dependency_or_bootstrap_hook()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "MyDmsVn.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var projects = new[] { "src", "tests" }.SelectMany(folder => Directory.GetFiles(Path.Combine(root!.FullName, folder), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains("MyDmsVn.Desktop.AppCore"));
        foreach (var project in projects)
        {
            var dependencies = System.Xml.Linq.XDocument.Load(project).Descendants("PackageReference").Select(e => (string?)e.Attribute("Include"));
            Assert.DoesNotContain(dependencies, value => value == "Dapper" || (value ?? "").StartsWith("RepoDb", StringComparison.OrdinalIgnoreCase));
            var directory = Path.GetDirectoryName(project)!;
            var legacyLock = Path.Combine(directory, "packages.lock.json");
            var net10Lock = Path.Combine(directory, "packages.vps04a.lock.json");
            var newHost = Path.GetFileNameWithoutExtension(project).Equals(
                "MyDmsVn.Server.AspNetCoreHost", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(project).Equals(
                    "MyDmsVn.Server.AspNetCoreHost.Tests", StringComparison.OrdinalIgnoreCase);
            // Legacy graph must remain locked; two new Linux-only projects use their
            // own reviewed net10 lockfiles without introducing a net48 package graph.
            Assert.True(File.Exists(legacyLock) || (newHost && File.Exists(net10Lock)));
            if (newHost) Assert.True(File.Exists(net10Lock));
            foreach (var lockFile in new[] { legacyLock, net10Lock }.Where(File.Exists))
            {
                Assert.DoesNotContain("\"Dapper\"", File.ReadAllText(lockFile));
                Assert.DoesNotContain("\"RepoDb", File.ReadAllText(lockFile));
            }
        }
        var assembly = typeof(MyDmsVn.Server.Infrastructure.Persistence.SqlUnitOfWorkFactory).Assembly;
        Assert.Null(assembly.GetType("MyDmsVn.Server.Infrastructure.Persistence.RepoDbMappingInitializer"));
        Assert.Null(assembly.GetType("MyDmsVn.Server.Infrastructure.Persistence.IRepoDbMapping"));
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name == "Dapper" || (a.Name ?? "").StartsWith("RepoDb", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(MyDmsVn.Server.Infrastructure.Persistence.SqlPersistenceBuilder).GetMethods(), m => m.Name == "AddRepoDbMapping");
        foreach (var file in Directory.GetFiles(Path.Combine(root!.FullName, "src", "MyDmsVn.Server.Application"), "*.cs", SearchOption.AllDirectories))
            Assert.DoesNotContain("IQueryable<", File.ReadAllText(file));
    }

}
