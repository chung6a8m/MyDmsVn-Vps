// Compiled only when Vps06aDiagnostic=missing; MSG0005 must be emitted by v3 generator.
using Mediator;

namespace MyDmsVn.Vps06A.MediatorCanary;

public sealed record CanaryMissingHandlerRequest() : IRequest<string>;
