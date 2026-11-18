using System;
using System.Threading;
using System.Threading.Tasks;

namespace MyDmsVn.Vps09.Acceptance;

internal static class Program
{
    public static Task<int> Main(string[] args) => CommandLine.RunAsync(
        args, new ProcessGitStateProvider(), Environment.GetEnvironmentVariable,
        Console.Out, Console.Error, CancellationToken.None);
}
