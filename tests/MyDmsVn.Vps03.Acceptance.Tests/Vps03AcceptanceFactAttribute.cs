using System;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03AcceptanceFactAttribute : FactAttribute
{
    public Vps03AcceptanceFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MYDMSVN_VPS03_ACCEPTANCE"),
            "approved", StringComparison.Ordinal))
            Skip = "NOT RUN: requires explicit operator-approved VPS-03 HTTPS/SQL acceptance environment.";
    }
}

internal sealed class Vps03AcceptanceTheoryAttribute : TheoryAttribute
{
    public Vps03AcceptanceTheoryAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("MYDMSVN_VPS03_ACCEPTANCE"),
            "approved", StringComparison.Ordinal))
            Skip = "NOT RUN: requires explicit operator-approved VPS-03 HTTPS/SQL acceptance environment.";
    }
}
