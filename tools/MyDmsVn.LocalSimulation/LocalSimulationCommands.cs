using System;
using System.Collections.Generic;

namespace MyDmsVn.LocalSimulation;

public static class LocalSimulationCommands
{
    private static readonly HashSet<string> Allowed = new(StringComparer.Ordinal)
    {
        "create",
        "backup-verify",
        "provision",
        "verify",
        "reset-admission",
    };

    public static bool IsAllowed(string command) => Allowed.Contains(command);
}
