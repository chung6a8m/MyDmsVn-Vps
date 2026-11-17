using System.Collections.Generic;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03AttendedGuiAcceptanceTests
{
    private static readonly string[] Required =
    {
        "VPS03.GUI.LOGIN", "VPS03.GUI.SHELL", "VPS03.GUI.CATALOG", "VPS03.GUI.GOODS",
        "VPS03.GUI.SALES", "VPS03.GUI.SECURITY", "VPS03.GUI.ERRORS", "VPS03.GUI.LOGOUT",
    };

    [Fact]
    public void Aborted_walkthrough_and_uncertain_cleanup_cannot_pass()
    {
        var accepted = new Dictionary<string, bool>();
        foreach (var id in Required) accepted[id] = true;
        Assert.False(new Vps03AttendedResult(new string('a', 40), accepted, true, true, true, aborted: true).Passed(Required));
        Assert.False(new Vps03AttendedResult(new string('a', 40), accepted, true, false, true, aborted: false).Passed(Required));
        accepted.Remove("VPS03.GUI.ERRORS");
        Assert.False(new Vps03AttendedResult(new string('a', 40), accepted, true, true, true, aborted: false).Passed(Required));
    }

    [Fact]
    public void Complete_operator_confirmation_and_cleanup_can_pass()
    {
        var accepted = new Dictionary<string, bool>();
        foreach (var id in Required) accepted[id] = true;
        Assert.True(new Vps03AttendedResult(new string('b', 40), accepted, true, true, true, aborted: false).Passed(Required));
    }
}
