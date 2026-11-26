using System;
using ErrorOr;
using Microsoft.AspNetCore.Mvc;
using MyDmsVn.Contracts;
using MyDmsVn.Server.Application;

namespace MyDmsVn.Server.HttpHost;

public sealed record V1SuccessEnvelope<T>(T Data);
public sealed record V1ErrorEnvelope(ApiError Error);

internal static class V1ApiResults
{
    internal static IActionResult Map<TSource, TResponse>(ErrorOr<TSource> result,
        Func<TSource, TResponse> map, string correlationId)
    {
        if (!result.IsError)
            return Json(200, new V1SuccessEnvelope<TResponse>(map(result.Value)));

        // Match WireApiController.Execute, including its closed operation
        // protocol mapping. Auth.* still belongs to Application error mapping.
        if (result.Errors.Count == 1 && !result.FirstError.Code.StartsWith("Auth.", StringComparison.Ordinal))
        {
            var status = result.FirstError.Code switch
            {
                "Operation.NotObserved" => 404,
                "Deployment.EpochMismatch" or "Idempotency.KeyConflict" or "Idempotency.FingerprintMismatch" => 409,
                "Operation.ResultExpired" => 410,
                "Operation.ResultUnavailable" or "Operation.Unavailable" => 503,
                _ => 0
            };
            if (status != 0)
                return Error(status, result.FirstError.Code,
                    "The protected operation is unavailable or cannot be disclosed.", correlationId);
        }
        var mapped = ApiResponseMapper.Map(result, "Request", correlationId);
        return Error(mapped.Error!);
    }

    internal static IActionResult Invalid(string? field, string correlationId) =>
        Error(new ApiError(ApiStatusCode.BadRequest, "ValidationError", "The request is invalid.",
            new[] { new ApiErrorDetail("Validation.InvalidValue", "The field or request format is invalid.", field) },
            correlationId));

    internal static IActionResult Error(int status, string code, string message, string correlationId) =>
        Error(new ApiError((ApiStatusCode)status, code, message, correlationId: correlationId));

    private static IActionResult Error(ApiError error) => Json((int)error.Status, new V1ErrorEnvelope(error));

    private static JsonResult Json(int status, object value) =>
        new(value) { StatusCode = status, ContentType = "application/json; charset=utf-8" };
}
