using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
#if VPS06B_MEDIATOR
using Mediator;
#else
using MediatR;
#endif
using MyDmsVn.Contracts.Security;

namespace MyDmsVn.Server.Application.Security;

public sealed class ListPermissionCatalogQuery
    : ApplicationRequest<PermissionCatalogDto>, IAuthorizedRequest
{
    public string PermissionKey => PermissionKeys.SecurityPermissionsRead;
}

internal sealed class ListPermissionCatalogQueryHandler
    : IRequestHandler<ListPermissionCatalogQuery, ErrorOr<PermissionCatalogDto>>
{
#if VPS06B_MEDIATOR
    ValueTask<ErrorOr<PermissionCatalogDto>> IRequestHandler<ListPermissionCatalogQuery, ErrorOr<PermissionCatalogDto>>.Handle(ListPermissionCatalogQuery message, CancellationToken cancellationToken)
        => new ValueTask<ErrorOr<PermissionCatalogDto>>(Handle(message, cancellationToken));
#endif

    private readonly IPermissionRegistry _registry;

    public ListPermissionCatalogQueryHandler(IPermissionRegistry registry)
    {
        _registry = registry;
    }

    public Task<ErrorOr<PermissionCatalogDto>> Handle(
        ListPermissionCatalogQuery request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<ErrorOr<PermissionCatalogDto>>(
            PermissionCatalogFactory.Create(_registry, PermissionCatalogFactory.GetApplicationVersion()));
    }
}
