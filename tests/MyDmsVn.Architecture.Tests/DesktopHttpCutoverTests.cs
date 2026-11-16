using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace MyDmsVn.Architecture.Tests
{
    public sealed class DesktopHttpCutoverTests
    {
        [Fact]
        public void Production_app_has_no_local_or_server_reference_or_startup()
        {
            var root = FindRoot();
            var xml = XDocument.Load(Path.Combine(root, "src", "MyDmsVn.Desktop.App",
                "MyDmsVn.Desktop.App.csproj"));
            var references = xml.Descendants("ProjectReference")
                .Select(x => (string?)x.Attribute("Include") ?? "").ToArray();
            Assert.Equal(2, references.Length);
            Assert.Contains(references, p => p.Contains("Desktop.Infrastructure.Http"));
            Assert.Contains(references, p => p.Contains("Desktop.WinForms"));
            Assert.DoesNotContain(references, p => p.Contains("Infrastructure.Local") ||
                p.Contains("Server"));
            var startup = File.ReadAllText(Path.Combine(root, "src",
                "MyDmsVn.Desktop.App", "Program.cs"));
            Assert.Contains("AddHttpDesktopAdapter", startup);
            Assert.DoesNotContain("AddSqlPersistence", startup);
            Assert.DoesNotContain("AddLocalDesktopAdapter", startup);
            Assert.DoesNotContain("DesktopDatabaseStartupCoordinator", startup);
        }

        [Fact]
        public void Sourcegrid_does_not_restore_sql_client_in_net48()
        {
            var root = FindRoot();
            var xml = XDocument.Load(Path.Combine(root, "vendor",
                "MyDmsVn.WinFormsKit", "vendor", "sourcegrid",
                "SourceGrid", "SourceGrid.csproj"));
            foreach (var package in new[] { "Microsoft.Data.SqlClient", "Microsoft.Windows.Compatibility" })
            {
                var node = xml.Descendants("PackageReference")
                    .Single(x => (string?)x.Attribute("Include") == package);
                Assert.Equal("'$(TargetFramework)' == 'net8.0-windows'",
                    (string?)node.Attribute("Condition"));
            }
            Assert.Contains(xml.Descendants("Compile"),
                x => ((string?)x.Attribute("Remove"))?.EndsWith("SqlCommandBuilder.cs",
                    StringComparison.OrdinalIgnoreCase) == true &&
                    ((string?)x.Attribute("Condition"))?.Contains("net48") == true);
        }

        private static string FindRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null &&
                !File.Exists(Path.Combine(directory.FullName, "MyDmsVn.sln")))
                directory = directory.Parent;
            return directory?.FullName ??
                throw new DirectoryNotFoundException("MyDmsVn.sln");
        }
    }
}
