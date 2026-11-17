using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03OwnedEnvironmentTests
{
    [Theory]
    [InlineData(11, "legacy-web11")]
    [InlineData(12, "web4")]
    [InlineData(13, "web5")]
    public void Maps_11_12_13_to_legacy_web11_web4_web5(int schema, string expected)
    {
        Assert.Equal(expected, Vps03OwnedEnvironment.ModeForSchema(schema));
    }

    [Fact]
    public void Desktop_child_environment_excludes_all_SQL_and_admin_values()
    {
        var source = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = "safe",
            ["MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING"] = "admin-secret",
            ["MYDMSVN_MIGRATION_SQLSERVER_CONNECTION_STRING"] = "migration-secret",
            ["MYDMSVN_RUNTIME_SQLSERVER_CONNECTION_STRING"] = "runtime-secret",
            ["MYDMSVN_WEB_SQL_CONNECTION_STRING"] = "web-secret",
            ["MYDMSVN_WEB_JWT_KEYS"] = "jwt-secret",
            ["MYDMSVN_GUI_P5_OWNER_PASSWORD"] = "password-secret",
        };

        var child = Vps03OwnedEnvironment.DesktopChildEnvironment(source,
            new Uri("https://localhost:44321"), "web5");

        Assert.Equal("safe", child["PATH"]);
        Assert.Equal("https://localhost:44321", child["MYDMSVN_DESKTOP_API_ORIGIN"]);
        Assert.Equal("web5", child["MYDMSVN_DESKTOP_OPERATION_MODE"]);
        Assert.DoesNotContain(child.Keys, key => key.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain(child.Keys, key => key.IndexOf("PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0);
        Assert.DoesNotContain(child.Keys, key => key.IndexOf("JWT", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Rejects_tls_authority_or_thumbprint_mismatch()
    {
        var expectedOrigin = new Uri("https://localhost:44321");
        const string expectedThumbprint = "00112233445566778899AABBCCDDEEFF00112233";

        Assert.Throws<InvalidDataException>(() => Vps03IisExpressHost.ValidateTls(expectedOrigin,
            expectedThumbprint, new Uri("https://localhost:44322"), expectedThumbprint));
        Assert.Throws<InvalidDataException>(() => Vps03IisExpressHost.ValidateTls(expectedOrigin,
            expectedThumbprint, expectedOrigin, "10112233445566778899AABBCCDDEEFF00112233"));
    }

    [Fact]
    public void Rejects_unrestricted_runtime_principal()
    {
        Assert.Throws<InvalidDataException>(() => Vps03SqlOracle.ValidateRuntimePrincipal(
            new Vps03RuntimePrincipalSnapshot(hasDesktopRole: true, isSysAdmin: true,
                isDbOwner: false, hasUnexpectedWrite: false)));
        Assert.Throws<InvalidDataException>(() => Vps03SqlOracle.ValidateRuntimePrincipal(
            new Vps03RuntimePrincipalSnapshot(hasDesktopRole: false, isSysAdmin: false,
                isDbOwner: false, hasUnexpectedWrite: false)));
        Assert.Throws<InvalidDataException>(() => Vps03SqlOracle.ValidateRuntimePrincipal(
            new Vps03RuntimePrincipalSnapshot(hasDesktopRole: true, isSysAdmin: false,
                isDbOwner: false, hasUnexpectedWrite: true)));
    }

    [Fact]
    public void Cleanup_failure_is_terminal_and_preserves_owned_diagnostics()
    {
        var directory = Path.Combine(Path.GetTempPath(), "vps03-cleanup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "sanitized.txt"), "owned process absence not proven");
        try
        {
            var result = Vps03OwnedCleanup.Complete(processAbsent: false, sqlAbsent: true, directory);

            Assert.False(result.Verified);
            Assert.Equal("failed", result.State);
            Assert.True(Directory.Exists(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
