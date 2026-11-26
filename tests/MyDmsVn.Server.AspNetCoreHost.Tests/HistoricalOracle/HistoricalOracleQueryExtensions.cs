// Test-only adapter for the unchanged historical source-linked parsers.
// No OWIN assembly, facade namespace or production service is involved.
using System.Net.Http;

namespace MyDmsVn.Server.HttpHost;

internal static class HistoricalOracleQueryExtensions
{
    internal static HistoricalContext GetOwinContext(this HttpRequestMessage request)
    {
        var query = request.RequestUri?.Query ?? "";
        return new(new(new(query.Length == 0 ? "" : query.Substring(1))));
    }

    internal sealed record HistoricalContext(HistoricalRequest Request);
    internal sealed record HistoricalRequest(HistoricalQuery QueryString);
    internal sealed record HistoricalQuery(string Value);
}
