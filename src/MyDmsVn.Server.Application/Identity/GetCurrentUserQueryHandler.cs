using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
#if VPS06B_MEDIATOR
using Mediator;
#else
using MediatR;
#endif
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.Application.Identity
{
    internal sealed class GetCurrentUserQueryHandler
        : IRequestHandler<GetCurrentUserQuery, ErrorOr<CurrentUserDto>>
    {
#if VPS06B_MEDIATOR
    // Retain the legacy Task implementation; expose native ValueTask for generated dispatch.
    ValueTask<ErrorOr<CurrentUserDto>> IRequestHandler<GetCurrentUserQuery, ErrorOr<CurrentUserDto>>.Handle(GetCurrentUserQuery message, CancellationToken cancellationToken)
        => new ValueTask<ErrorOr<CurrentUserDto>>(Handle(message, cancellationToken));
#endif

        private readonly ICurrentUserAccessor _currentUserAccessor;
        private readonly IUserAccountValidator _accountValidator;

        public GetCurrentUserQueryHandler(
            ICurrentUserAccessor currentUserAccessor,
            IUserAccountValidator accountValidator)
        {
            _currentUserAccessor = currentUserAccessor;
            _accountValidator = accountValidator;
        }

        public async Task<ErrorOr<CurrentUserDto>> Handle(
            GetCurrentUserQuery request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentUser = _currentUserAccessor.Current;
            if (!currentUser.IsAuthenticated ||
                !currentUser.IsActive ||
                !currentUser.UserId.HasValue ||
                currentUser.Username == null ||
                currentUser.DisplayName == null)
            {
                return
                    Error.Unauthorized(
                        "Auth.Unauthorized",
                        "Authentication is required.");
            }

            if (!await _accountValidator
                    .IsActiveAsync(currentUser.UserId.Value, cancellationToken)
                    .ConfigureAwait(false))
            {
                return Error.Unauthorized(
                    "Auth.Unauthorized",
                    "Authentication is required.");
            }

            return
                new CurrentUserDto(
                    currentUser.UserId.Value,
                    currentUser.Username,
                    currentUser.DisplayName);
        }
    }
}
