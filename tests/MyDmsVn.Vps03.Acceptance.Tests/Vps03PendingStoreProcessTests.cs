using System;
using System.IO;
using MyDmsVn.Desktop.Infrastructure.Http;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03PendingStoreProcessTests
{
    [Fact]
    [Trait("Category", "Vps03Issue7Crash")]
    [Trait("CaseId", "VPS03.ISSUE7.CRASH.BEFORE-SAVE")]
    public void Crash_before_save_leaves_no_admitted_intent_or_dispatch()
    {
        var origin = UniqueOrigin();
        var record = Record();
        var exchange = Vps03PendingStoreProcess.CreateExchangeDirectory();
        try
        {
            using var child = Vps03PendingStoreProcess.Start("before-save", origin, exchange, record);
            child.WaitForStage("before-save-ready", TimeSpan.FromSeconds(15));
            child.KillAndWait(TimeSpan.FromSeconds(15));

            Assert.Equal(0, child.IntentCount);
            Assert.False(child.DispatchMarkerExists);
        }
        finally
        {
            Vps03PendingStoreProcess.Cleanup(origin, exchange, record);
        }
    }

    [Fact]
    [Trait("Category", "Vps03Issue7Crash")]
    [Trait("CaseId", "VPS03.ISSUE7.CRASH.AFTER-SAVE")]
    public void Crash_after_atomic_save_is_loaded_by_a_fresh_process()
    {
        var origin = UniqueOrigin();
        var record = Record();
        var exchange = Vps03PendingStoreProcess.CreateExchangeDirectory();
        try
        {
            using (var child = Vps03PendingStoreProcess.Start("after-save", origin, exchange, record))
            {
                child.WaitForStage("after-save-complete", TimeSpan.FromSeconds(15));
                child.KillAndWait(TimeSpan.FromSeconds(15));
                Assert.Equal(1, child.IntentCount);
                Assert.False(child.DispatchMarkerExists);
            }

            using var loader = Vps03PendingStoreProcess.Start("load", origin, exchange, record);
            var loaded = loader.WaitForLoad(TimeSpan.FromSeconds(15));
            Assert.Equal(record.OperationId, loaded.OperationId);
            Assert.Equal(record.Fingerprint, loaded.Fingerprint);
            Assert.Null(loaded.FailureCode);
            Assert.True(loader.WaitForExit(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            Vps03PendingStoreProcess.Cleanup(origin, exchange, record);
        }
    }

    private static string UniqueOrigin() =>
        "https://issue7-" + Guid.NewGuid().ToString("N") + ".invalid";

    private static HttpPendingWriteRecord Record() => new()
    {
        OperationId = Guid.NewGuid().ToString("D"),
        ActorUserId = 7301,
        Protocol = "web5",
        Kind = "Sales.Post",
        Method = "POST",
        Route = "/api/v1/sales-receipts/7301/post",
        TargetId = 7301,
        DeploymentEpoch = Guid.NewGuid().ToString("D"),
        Fingerprint = new string('a', 64),
        CreatedUtc = DateTime.UtcNow,
    };
}
