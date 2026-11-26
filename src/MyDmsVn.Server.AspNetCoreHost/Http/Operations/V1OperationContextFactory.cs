using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using MyDmsVn.Server.Application.Operations;

namespace MyDmsVn.Server.HttpHost;

public interface IV1OperationContextFactory
{
    ValueTask<WebOperationContext?> CreateAsync(HttpContext context, WebOperationKind kind,
        long? targetId, CancellationToken cancellationToken);
}

internal sealed class V1OperationContextFactory : IV1OperationContextFactory
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private const string Key = "Idempotency-Key";
    private const string Epoch = "X-MyDmsVn-Deployment-Epoch";
    private const string Fingerprint = "X-MyDmsVn-Operation-Fingerprint";

    public ValueTask<WebOperationContext?> CreateAsync(HttpContext context, WebOperationKind kind,
        long? targetId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var request = context.Request;
        var options = context.RequestServices.GetRequiredService<WebHostOptions>();
        if (!options.Web4Enabled)
        {
            if (request.Headers.ContainsKey(Key) || request.Headers.ContainsKey(Epoch) || request.Headers.ContainsKey(Fingerprint))
                throw new V1BindingException();
            return ValueTask.FromResult<WebOperationContext?>(null);
        }

        var session = context.RequestServices.GetRequiredService<RequestIdentityAdmission>().SessionId ?? throw new WebOperationException("Auth.Unauthorized");
        var operationId = ReadGuid(request, Key, v4: true);
        var epoch = ReadGuid(request, Epoch, v4: false);
        var canonical = V1OperationCanonicalizer.Canonicalize(kind, targetId, V1ValidatedRequestSource.Get(context));
        var description = V1OperationCanonicalizer.Describe(kind);
        var path = description.Route.Replace("{saleId}", targetId?.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        // Do not accept encoded aliases, dot segments, query strings or trailing slash.
        var raw = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        if (!string.Equals(request.Method, description.Method, StringComparison.Ordinal) ||
            !string.Equals(raw, path, StringComparison.Ordinal)) throw new V1BindingException();

        return ValueTask.FromResult<WebOperationContext?>(new WebOperationContext(operationId, epoch, session, kind,
            targetId, SHA256.HashData(Utf8.GetBytes(canonical)), new WebOperationResultCodec()));
    }

    private static Guid ReadGuid(HttpRequest request, string name, bool v4)
    {
        if (!request.Headers.TryGetValue(name, out var values) || values.Count != 1)
            throw new V1BindingException();
        var text = values[0];
        if (text == null || !Guid.TryParseExact(text, "D", out var guid) || guid == Guid.Empty ||
            !string.Equals(guid.ToString("D"), text, StringComparison.Ordinal) ||
            v4 && (text[14] != '4' || "89ab".IndexOf(text[19]) < 0))
            throw new V1BindingException();
        return guid;
    }
}
