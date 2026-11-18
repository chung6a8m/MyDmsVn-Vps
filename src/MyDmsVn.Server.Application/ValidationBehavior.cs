using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using FluentValidation;
using FluentValidation.Results;
#if VPS06B_MEDIATOR
using Mediator;
#else
using MediatR;
#endif

namespace MyDmsVn.Server.Application
{
    public sealed class ValidationBehavior<TRequest, TResponse>
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull, IRequest<TResponse>, IApplicationErrorResponse<TResponse>
    {
        private const string FieldMetadataKey = "Field";
        private readonly IReadOnlyCollection<IValidator<TRequest>> _validators;

        public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
        {
            _validators = validators.ToArray();
        }

#if VPS06B_MEDIATOR
        public async ValueTask<TResponse> Handle(
            TRequest request,
            MessageHandlerDelegate<TRequest, TResponse> next,
            CancellationToken cancellationToken)
#else
        public async Task<TResponse> Handle(
            TRequest request,
            RequestHandlerDelegate<TResponse> next,
            CancellationToken cancellationToken)
#endif
        {
            if (_validators.Count == 0)
            {
                return await InvokeNext(next, request, cancellationToken).ConfigureAwait(false);
            }

            var failures = new List<ValidationFailure>();
            foreach (var validator in _validators)
            {
                var validationResult = await validator.ValidateAsync(
                    new ValidationContext<TRequest>(request),
                    cancellationToken).ConfigureAwait(false);
                failures.AddRange(
                    validationResult.Errors.Where(failure => failure != null));
            }

            if (failures.Count == 0)
            {
                return await InvokeNext(next, request, cancellationToken).ConfigureAwait(false);
            }

            var errors = failures
                .Select(failure => Error.Validation(
                    string.IsNullOrWhiteSpace(failure.ErrorCode)
                        ? "Validation.Error"
                        : failure.ErrorCode,
                    failure.ErrorMessage,
                    new Dictionary<string, object>
                    {
                        [FieldMetadataKey] = failure.PropertyName,
                    }))
                .ToArray();

            return request.FromErrors(errors);
        }
#if VPS06B_MEDIATOR
        private static ValueTask<TResponse> InvokeNext(
            MessageHandlerDelegate<TRequest, TResponse> next,
            TRequest request,
            CancellationToken cancellationToken)
            => next(request, cancellationToken);
#else
        private static Task<TResponse> InvokeNext(
            RequestHandlerDelegate<TResponse> next,
            TRequest request,
            CancellationToken cancellationToken)
            => next();
#endif

    }
}
