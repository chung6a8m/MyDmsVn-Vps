// Compiled only when Vps06aDiagnostic=duplicate; MSG0001 must be emitted by v3 generator.
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;
using Mediator;

namespace MyDmsVn.Vps06A.MediatorCanary;

public sealed class CanaryDuplicateHandler : IRequestHandler<CanaryRequest, ErrorOr<string>>
{
    public ValueTask<ErrorOr<string>> Handle(CanaryRequest request, CancellationToken cancellationToken)
    {
        ErrorOr<string> result = "duplicate";
        return ValueTask.FromResult(result);
    }
}
