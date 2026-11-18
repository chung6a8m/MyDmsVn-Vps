using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using FluentValidation;
using FluentValidation.Results;
using Mediator;

namespace MyDmsVn.Vps06A.MediatorCanary;

// An independent, deliberately bounded contract mirror. The application/HTTP host still uses MediatR.
public sealed class CanaryRequest : IRequest<ErrorOr<string>>
{
    public CanaryRequest(decimal quantity) => Lines = new[] { new CanaryLine(quantity) };
    public IReadOnlyList<CanaryLine> Lines { get; }
    public string Mode { get; init; } = "success";
    public string? OperationCode { get; init; }
    public bool Audit { get; init; } = true;
    public int? AuthorizedUserId { get; private set; }
    public void BindActor(int userId)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        AuthorizedUserId = userId;
    }
    public ErrorOr<string> FromErrors(IEnumerable<Error> errors) =>
        ErrorOrFactory.From<string>(errors.ToList());
}
public sealed record CanaryLine(decimal Quantity);
public sealed class CanaryOperationException : Exception
{
    public CanaryOperationException(string code) : base("operation error") => Code = code;
    public string Code { get; }
}

public sealed class CanaryScope : IDisposable
{
    public Guid Id { get; } = Guid.NewGuid();
    public bool Disposed { get; private set; }
    public CancellationTokenSource? CancelOnValidation { get; set; }
    public void Dispose() => Disposed = true;
    public bool Authenticated { get; set; } = true;
    public bool Active { get; set; } = true;
    public int? UserId { get; set; } = 101;
    public bool Permission { get; set; } = true;
    public string CorrelationId { get; set; } = "canary-default";
    public List<string> Events { get; } = new();
    public List<string> Audits { get; } = new();
    public int ReportedExceptions { get; set; }
}

public sealed class ValidatorOverlapProbe
{
    private int _active;
    public bool Overlapped { get; private set; }
    public async Task<bool> CheckAsync(string label, bool valid, CanaryScope state, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _active) > 1) Overlapped = true;
        state.Events.Add(label);
        state.CancelOnValidation?.Cancel();
        try
        {
            await Task.Delay(4, cancellationToken).ConfigureAwait(false);
            return valid;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}

public sealed class PositiveQuantityValidator : AbstractValidator<CanaryRequest>
{
    public PositiveQuantityValidator(ValidatorOverlapProbe probe, CanaryScope state)
    {
        RuleForEach(request => request.Lines).ChildRules(line =>
            line.RuleFor(item => item.Quantity)
                .MustAsync((quantity, token) => probe.CheckAsync("validator-positive", quantity > 0m, state, token))
                .WithErrorCode("Validation.Positive")
                .WithMessage("Quantity must be positive."));
    }
}
public sealed class RequiredLineValidator : AbstractValidator<CanaryRequest>
{
    public RequiredLineValidator(ValidatorOverlapProbe probe, CanaryScope state)
    {
        RuleForEach(request => request.Lines)
            .MustAsync((line, token) => probe.CheckAsync("validator-line", line.Quantity > 0m, state, token))
            .WithErrorCode("Validation.LineRequired")
            .WithMessage("A valid line is required.");
    }
}

// Internal handler visibility must work in the generator's outermost compilation.
internal sealed class CanaryHandler : IRequestHandler<CanaryRequest, ErrorOr<string>>
{
    private readonly CanaryScope _scope;
    public CanaryHandler(CanaryScope scope) => _scope = scope;
    public ValueTask<ErrorOr<string>> Handle(CanaryRequest request, CancellationToken cancellationToken)
    {
        _scope.Events.Add("handler");
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Mode == "throw") throw new InvalidOperationException("secret SQL connection detail");
        if (request.Mode == "operation") throw new CanaryOperationException(request.OperationCode ?? "private-diagnostic");
        if (request.Mode == "cancel") throw new OperationCanceledException(cancellationToken);
        ErrorOr<string> result = $"{request.AuthorizedUserId}|{_scope.CorrelationId}|{_scope.Id:D}";
        return ValueTask.FromResult(result);
    }
}

public sealed class CanaryExceptionBehavior : IPipelineBehavior<CanaryRequest, ErrorOr<string>>
{
    private readonly CanaryScope _scope;
    public CanaryExceptionBehavior(CanaryScope scope) => _scope = scope;

    public async ValueTask<ErrorOr<string>> Handle(
        CanaryRequest message,
        MessageHandlerDelegate<CanaryRequest, ErrorOr<string>> next,
        CancellationToken cancellationToken)
    {
        _scope.Events.Add("exception");
        try
        {
            return await next(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (CanaryOperationException ex)
        {
            var error = ex.Code switch
            {
                "Auth.Unauthorized" => Error.Unauthorized(ex.Code, "Authentication is required."),
                "Auth.Forbidden" => Error.Forbidden(ex.Code, "The current operation permission is required."),
                "Idempotency.KeyConflict" or "Deployment.EpochMismatch" or
                "Operation.ResultExpired" or "Operation.ResultUnavailable"
                    => Error.Conflict(ex.Code, "The protected operation cannot execute or return its result."),
                "Operation.Unavailable" => Error.Unexpected(ex.Code, "The operation is unavailable."),
                _ => Error.Unexpected("InternalError", "An unexpected error occurred.")
            };
            return message.FromErrors(new[] { error });
        }
        catch (Exception)
        {
            _scope.ReportedExceptions++;
            return message.FromErrors(new[] { Error.Unexpected("InternalError", "An unexpected error occurred.") });
        }
    }
}

public sealed class CanaryAuthorizationBehavior : IPipelineBehavior<CanaryRequest, ErrorOr<string>>
{
    private readonly CanaryScope _scope;
    public CanaryAuthorizationBehavior(CanaryScope scope) => _scope = scope;

    public ValueTask<ErrorOr<string>> Handle(
        CanaryRequest message,
        MessageHandlerDelegate<CanaryRequest, ErrorOr<string>> next,
        CancellationToken cancellationToken)
    {
        _scope.Events.Add("authorization");
        if (!_scope.Authenticated || !_scope.Active || !_scope.UserId.HasValue)
        {
            Audit(message, "Denied", null);
            return ValueTask.FromResult(message.FromErrors(
                new[] { Error.Unauthorized("Auth.Unauthorized", "Authentication is required.") }));
        }
        if (!_scope.Permission)
        {
            Audit(message, "Denied", _scope.UserId);
            return ValueTask.FromResult(message.FromErrors(
                new[] { Error.Forbidden("Auth.Forbidden", "The operation is not permitted.") }));
        }
        Audit(message, "Admitted", _scope.UserId);
        message.BindActor(_scope.UserId.Value);
        return next(message, cancellationToken);
    }

    private void Audit(CanaryRequest request, string outcome, int? actor)
    {
        if (!request.Audit) return;
        _scope.Events.Add(outcome == "Admitted" ? "audit-admitted" : "audit-denied");
        _scope.Audits.Add($"{outcome}:{actor?.ToString() ?? "null"}:{_scope.CorrelationId}");
    }
}

public sealed class CanaryValidationBehavior : IPipelineBehavior<CanaryRequest, ErrorOr<string>>
{
    private readonly IReadOnlyList<IValidator<CanaryRequest>> _validators;
    private readonly CanaryScope _scope;
    public CanaryValidationBehavior(IEnumerable<IValidator<CanaryRequest>> validators, CanaryScope scope)
    {
        _validators = validators.ToArray();
        _scope = scope;
    }

    public async ValueTask<ErrorOr<string>> Handle(
        CanaryRequest message,
        MessageHandlerDelegate<CanaryRequest, ErrorOr<string>> next,
        CancellationToken cancellationToken)
    {
        _scope.Events.Add("validation");
        var failures = new List<ValidationFailure>();
        foreach (var validator in _validators)
        {
            var result = await validator.ValidateAsync(new ValidationContext<CanaryRequest>(message), cancellationToken)
                .ConfigureAwait(false);
            failures.AddRange(result.Errors.Where(error => error != null));
        }
        if (failures.Count == 0)
            return await next(message, cancellationToken).ConfigureAwait(false);

        return message.FromErrors(failures.Select(failure => Error.Validation(
            string.IsNullOrWhiteSpace(failure.ErrorCode) ? "Validation.Error" : failure.ErrorCode,
            failure.ErrorMessage,
            new Dictionary<string, object> { ["Field"] = failure.PropertyName })));
    }
}
