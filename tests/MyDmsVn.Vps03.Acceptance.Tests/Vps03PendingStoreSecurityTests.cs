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
    public void Clear_removes_only_the_exact_matching_record()
    {
        using var fixture = Vps03PendingStoreFixture.Create();
        fixture.SaveInChild();
        var mismatch = fixture.CloneRecord();
        mismatch.Fingerprint = new string('b', 64);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.Clear(mismatch));
        Assert.Equal("Desktop.PendingWriteChanged", error.Message);
        Assert.True(fixture.IntentExists);

        fixture.Clear(fixture.Record);
        Assert.False(fixture.IntentExists);
    }
}
