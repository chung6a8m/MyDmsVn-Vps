# VPS-10.1 — Host composition, configuration and startup checks

## Scope

Owner authorization: OWN-VPS-05 and the VPS-10.1 roadmap. Base: `7f13c267f3eeb85985f74c53b6b49091689465d6` (main, merge of PR #33). Paired Desktop source is unchanged from this base; Server remains net10.0 and Desktop net48. No project target, package lock, SQL schema, route inventory, contract corpus, business code or release policy change. All 57 routes remain bridge-owned.

- `Program` retains the SQL-free `--list-routes` early exit, sanitized startup failure and mandatory scope validation; builds the host, awaits explicit startup checks before `RunAsync`, disposes a failed app and retains proxy/routing/admission/endpoint order.
- `HostOptionsRegistration` snapshots only the existing exact deployment environment keys. The shared legacy `WebHostOptions` parser has a reader overload; net48 `FromEnvironment` delegates to it without changing the flags, defaults, grammar or deployment authority. Do not replace that snapshot with the general appsettings/CLI configuration.
- `AddVps10HostServices(IServiceCollection, IConfiguration)` composes the same Application, scoped identity, generated Mediator, readiness, audit and SQL graph. Existing SQL schema/principal/superuser implementations are registered by their existing interfaces; no replacement validation algorithm. Protected audit singleton is resolved explicitly before listening, after SQL checks.
- `HostStartupChecks.ValidateAsync(IServiceProvider, CancellationToken)` has the existing shared 30-second SQL budget, schema -> runtime principal -> superuser -> protected audit, fail-closed. No hosted/background validation. General ASP.NET hosting configuration and direct HTTPS remain unchanged.
- 26 targeted test cases are added to the existing Linux lane, with a separate exact-count TRX assertion and unchanged 37 characterization / 293 differential gates. Startup refusal tests substitute only SQL checks with deterministic probes; the real protected sink is used for missing-file rejection. Real loopback Kestrel exercises the production pipeline with synthetic SQL checks, not a real SQL acceptance claim.

## Evidence and pending gates

| Gate | Status at source commit |
| --- | --- |
| Source inspection against pinned base, review of existing interfaces and workflow selectors | SOURCE PREPARED; independent review pending |
| Local .NET SDK 10.0.404 / checkout / G1/G2 | NOT RUN — editing environment has no SDK or clone network |
| CI Ubuntu 24.04, SDK 10.0.404 locked restore/build, full application/host suites, targeted 26 composition and 37 characterization, 57 route inventory, 293 differential, runtime boundary | PENDING — inspect final-HEAD workflow and TRX |
| Existing Windows net48 build/test/discovery and Desktop artifact | PENDING — unchanged source pair, no new runtime claim |
| Disposable SQL regression / protected staging / attended GUI | PENDING existing disposable CI; protected and attended not verified |

Targeted command (from `src/MyDmsVn.Server.AspNetCoreHost`, after locked restore and Release build as specified by G1):

```bash
dotnet test ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true -p:Vps06bMediator=true -c Release -f net10.0 --no-build --no-restore --filter FullyQualifiedName~Vps10HostCompositionTests --logger 'trx;LogFileName=vps10-composition.trx'
```

No tests reported executed/passed/failed/skipped locally. Final head SHA, run URLs, actual counts, review disposition and any follow-up fixes belong in the implementation PR. No production readiness or VPS-08/09 acceptance implied.
