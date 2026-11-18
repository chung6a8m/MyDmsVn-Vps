using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace MyDmsVn.Server.FluentMigrator;

/// <summary>
/// Reads original pinned DbUp SQL. GO is a client-side batch delimiter, not T-SQL.
/// Nothing inside SQL batches is rewritten, normalized, or inferred by filename glob.
/// </summary>
public static class ImmutableSqlScript
{
    public const string ResourcePrefix = "MyDmsVn.Server.FluentMigrator.Scripts.";

    private static readonly Regex GoSeparator = new Regex(
        @"^[ \t]*GO[ \t]*(?:--[^\r\n]*)?(?:\r\n|\n|\r|$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Read(string scriptName)
    {
        if (string.IsNullOrWhiteSpace(scriptName) ||
            scriptName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
            !scriptName.EndsWith(".sql", StringComparison.Ordinal))
            throw new ArgumentException("Unrecognized historical SQL resource.", nameof(scriptName));

        using var stream = typeof(ImmutableSqlScript).Assembly
            .GetManifestResourceStream(ResourcePrefix + scriptName)
            ?? throw new InvalidOperationException("A pinned SQL migration resource is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static IReadOnlyList<string> Batches(string scriptName)
    {
        var sql = Read(scriptName);
        if (string.IsNullOrWhiteSpace(sql) || sql.IndexOf('\0') >= 0)
            throw new InvalidOperationException("A pinned SQL migration is empty or invalid.");

        var batches = new List<string>();
        var offset = 0;
        foreach (Match separator in GoSeparator.Matches(sql))
        {
            var batch = sql.Substring(offset, separator.Index - offset);
            if (!string.IsNullOrWhiteSpace(batch))
                batches.Add(batch);
            offset = separator.Index + separator.Length;
        }

        var last = sql.Substring(offset);
        if (!string.IsNullOrWhiteSpace(last))
            batches.Add(last);
        if (batches.Count == 0)
            throw new InvalidOperationException("A pinned SQL migration contains no executable batch.");
        return batches.AsReadOnly();
    }
}
