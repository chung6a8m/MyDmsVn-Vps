using System;
using MyDmsVn.Desktop.Infrastructure.Http;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03PendingStoreSecurityTests
{
    [Fact]
    [Trait("Category", "Vps03Issue7FileSecurity")]
    [Trait("CaseId", "VPS03.ISSUE7.FILE.CORRUPT")]
    public void Bounded_corrupt_ciphertext_fails_closed()
    {
        using var fixture = Vps03PendingStoreFixture.Create();
        fixture.SaveInChild();
        fixture.ReplaceCiphertext(new byte[] { 0x13, 0x37, 0x00, 0xff });

        var result = fixture.LoadInChild();

        Assert.Equal("Desktop.PendingIntentCorrupt", result.FailureCode);
        Assert.True(fixture.IntentExists);
    }

    [Fact]
    [Trait("Category", "Vps03Issue7FileSecurity")]
    [Trait("CaseId", "VPS03.ISSUE7.FILE.ACL")]
    public void Owner_file_with_weakened_acl_fails_closed()
    {
        using var fixture = Vps03PendingStoreFixture.Create();
        fixture.SaveInChild();
        fixture.WeakenAcl();

        var result = fixture.LoadInChild();

        Assert.Equal("Desktop.PendingIntentCorrupt", result.FailureCode);
        Assert.True(fixture.IntentExists);
    }

    [Fact]
    [Trait("Category", "Vps03Issue7FileSecurity")]
    [Trait("CaseId", "VPS03.ISSUE7.FILE.ORIGIN")]
    public void Ciphertext_copied_to_another_origin_fails_closed()
    {
        using var fixture = Vps03PendingStoreFixture.Create();
        fixture.SaveInChild();
        var otherOrigin = Vps03PendingStoreFixture.UniqueOrigin();
        fixture.CopyToOrigin(otherOrigin);

        var result = fixture.LoadInChild(otherOrigin);

        Assert.Equal("Desktop.PendingIntentCorrupt", result.FailureCode);
        Assert.True(fixture.IntentExistsAt(otherOrigin));
    }

    [Fact]
    [Trait("Category", "Vps03Issue7FileSecurity")]
    [Trait("CaseId", "VPS03.ISSUE7.FILE.EXACT-CLEAR")]
    public void Clear_removes_only_the_exact_matching_record_and_cannot_delete_its_replacement()
    {
        using var fixture = Vps03PendingStoreFixture.Create();
        fixture.SaveInChild();
        var mismatch = fixture.CloneRecord();
        mismatch.Fingerprint = new string('b', 64);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Clear(mismatch));
        Assert.Equal("Desktop.PendingWriteChanged", error.Message);
        Assert.True(fixture.IntentExists);

        var replacement = fixture.CloneRecord();
        replacement.OperationId = Guid.NewGuid().ToString("D");
        replacement.Fingerprint = new string('c', 64);
        var clearExchange = Vps03PendingStoreProcess.CreateExchangeDirectory();
        var replaceExchange = Vps03PendingStoreProcess.CreateExchangeDirectory();
        try
        {
            using var clearer = Vps03PendingStoreProcess.Start(
                "clear-paused", fixture.Origin, clearExchange, fixture.Record);
            clearer.WaitForStage("clear-validated", TimeSpan.FromSeconds(15));

            using var replacer = Vps03PendingStoreProcess.Start(
                "replace", fixture.Origin, replaceExchange, replacement);
            replacer.AssertStageAbsent("replace-complete", TimeSpan.FromMilliseconds(750));

            clearer.SignalRelease();
            clearer.WaitForStage("clear-complete", TimeSpan.FromSeconds(15));
            replacer.WaitForStage("replace-complete", TimeSpan.FromSeconds(15));

            using var loader = Vps03PendingStoreProcess.Start(
                "load", fixture.Origin, replaceExchange, replacement);
            var loaded = loader.WaitForLoad(TimeSpan.FromSeconds(15));
            Assert.Equal(replacement.OperationId, loaded.OperationId);
            Assert.Equal(replacement.Fingerprint, loaded.Fingerprint);
        }
        finally
        {
            Vps03PendingStoreProcess.Cleanup(fixture.Origin, clearExchange, replacement);
            Vps03PendingStoreProcess.Cleanup(fixture.Origin, replaceExchange, replacement);
        }
    }
}
