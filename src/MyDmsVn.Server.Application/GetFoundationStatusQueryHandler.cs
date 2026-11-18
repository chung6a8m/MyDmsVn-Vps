using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
#if VPS06B_MEDIATOR
using Mediator;
#else
using MediatR;
#endif
using MyDmsVn.Contracts;

namespace MyDmsVn.Server.Application
{
    internal sealed class GetFoundationStatusQueryHandler
        : IRequestHandler<GetFoundationStatusQuery, ErrorOr<FoundationStatus>>
    {
#if VPS06B_MEDIATOR
    // Retain the legacy Task implementation; expose native ValueTask for generated dispatch.
    ValueTask<ErrorOr<FoundationStatus>> IRequestHandler<GetFoundationStatusQuery, ErrorOr<FoundationStatus>>.Handle(GetFoundationStatusQuery message, CancellationToken cancellationToken)
        => new ValueTask<ErrorOr<FoundationStatus>>(Handle(message, cancellationToken));
#endif

        private readonly IFoundationProbe _probe;

        public GetFoundationStatusQueryHandler(IFoundationProbe probe)
        {
            _probe = probe;
        }

        public Task<ErrorOr<FoundationStatus>> Handle(
            GetFoundationStatusQuery request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ErrorOr<FoundationStatus> result = _probe.GetStatus();
            return Task.FromResult(result);
        }
    }
}
