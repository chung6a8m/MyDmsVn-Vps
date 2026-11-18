using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
#if VPS06B_MEDIATOR
using Mediator;
#else
using MediatR;
#endif
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.Application.Security
{
    internal sealed class CheckPermissionQueryHandler
        : IRequestHandler<CheckPermissionQuery, ErrorOr<PermissionDecisionDto>>
    {
#if VPS06B_MEDIATOR
    ValueTask<ErrorOr<PermissionDecisionDto>> IRequestHandler<CheckPermissionQuery, ErrorOr<PermissionDecisionDto>>.Handle(CheckPermissionQuery message, CancellationToken cancellationToken)
        => new ValueTask<ErrorOr<PermissionDecisionDto>>(Handle(message, cancellationToken));
#endif

        private readonly IPermissionAuthorizationService _authorization;

        public CheckPermissionQueryHandler(IPermissionAuthorizationService authorization)
        {
            _authorization = authorization;
        }

        public async Task<ErrorOr<PermissionDecisionDto>> Handle(
            CheckPermissionQuery request,
            CancellationToken cancellationToken)
        {
            var isAllowed = await _authorization
                .IsAllowedAsync(request.PermissionKey, cancellationToken)
                .ConfigureAwait(false);
            return new PermissionDecisionDto(request.PermissionKey, isAllowed);
        }
    }
}
