using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
#if VPS06B_MEDIATOR
using Mediator;
#else
using MediatR;
#endif

namespace MyDmsVn.Server.Application
{
    public sealed class ExceptionHandlingBehavior<TRequest, TResponse>
        : IPipelineBehavior<TRequest, TResponse>
        where TRequest : notnull, IRequest<TResponse>, IApplicationErrorResponse<TResponse>
    {
        private readonly IApplicationExceptionReporter _exceptionReporter;

        public ExceptionHandlingBehavior(IApplicationExceptionReporter exceptionReporter)
        {
            _exceptionReporter = exceptionReporter;
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
            try
            {
                return await InvokeNext(next, request, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Operations.WebOperationException exception)
            {
                var error = exception.Code switch
                {
                    "Auth.Unauthorized" => Error.Unauthorized(exception.Code, "Authentication is required."),
                    "Auth.Forbidden" => Error.Forbidden(exception.Code, "The current operation permission is required."),
                    "Idempotency.KeyConflict" or "Deployment.EpochMismatch" or "Operation.ResultExpired" or "Operation.ResultUnavailable"
                        => Error.Conflict(exception.Code, "The protected operation cannot execute or return its result."),
                    "Operation.Unavailable" => Error.Unexpected(exception.Code, "The operation is unavailable."),
                    _ => Error.Unexpected("InternalError", "An unexpected error occurred."),
                };
                return request.FromErrors(new[] { error });
            }
            catch (Exception exception)
            {
                _exceptionReporter.Report(typeof(TRequest), exception);
                return request.FromErrors(
                    new[]
                    {
                        Error.Unexpected(
                            "InternalError",
                            "An unexpected error occurred."),
                    });
            }
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
