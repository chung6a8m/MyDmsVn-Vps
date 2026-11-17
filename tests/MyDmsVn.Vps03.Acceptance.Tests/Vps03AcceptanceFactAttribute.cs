using System;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03AcceptanceFactAttribute : FactAttribute
{
    internal static bool IsApproved => string.Equals(
        Environment.GetEnvironmentVariable("MYDMSVN_VPS03_ACCEPTANCE"),
        "approved", StringComparison.Ordinal);

    public Vps03AcceptanceFactAttribute()
    {
        if (!IsApproved)
            Skip = "NOT RUN: requires explicit operator-approved VPS-03 HTTPS/SQL acceptance environment.";
    }
}

internal sealed class Vps03AcceptanceTheoryAttribute : TheoryAttribute
{
    public Vps03AcceptanceTheoryAttribute()
    {
        if (!Vps03AcceptanceFactAttribute.IsApproved)
            Skip = "NOT RUN: requires explicit operator-approved VPS-03 HTTPS/SQL acceptance environment.";
    }
}
