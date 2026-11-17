# ASP.NET Core 10 transport bridge — VPS-04A

This path ports the existing Web API 2 HTTP surface to a native Kestrel endpoint dispatcher **without rewriting the 57 frozen controllers** in the same increment as the runtime migration.

## Repository topology

- `src/MyDmsVn.Server.AspNetCoreHost`: new ASP.NET Core 10 Web SDK host, source-linked existing JSON/wire/controller code; explicitly excludes legacy `Startup`, `Web2Pipeline`, `SpaAssets`, `RequestServices` and Windows ACL-backed audit implementation.
- `CoreApiBoundary`: per-request checks (HTTPS/Origin/Host, denial of untrusted proxy headers, body size, correlation, anonymous allowlist, JWT+SQL session), including fail-closed cleanup of staged headers before writing an unexpected-error 500.
- `Net10RouteTable`: reflects controller route attributes, rejects count/duplicate drift, maps ASP.NET Core endpoints preserving `Route.Order`, and protects known-path HTTP method misses with JSON 405 ahead of the catch-all JSON 404. Compiled parity compares verb/path/controller/action, while preserving raw wire parser/response codec semantics.
- `CompatWebApi`: temporary facade for the small fraction of legacy controller surface relying on Web API 2/OWIN types. It is **not** an OWIN runtime.
- `CoreRequestServices`: maps request identity/correlation to ASP.NET Core request scopes.
- `Program`: preflight schema/principal/superuser, configure SQL persistence and MediatR pipeline, Kestrel startup.

## Scope and known differences

1. Route shape, behavior and JSON serialization remain the legacy contracts; the bridge does not change decimal, date, idempotency or fingerprint algorithms.
2. Do not trust `Forwarded` or `X-Forwarded-*` until VPS-04B defines an authenticated reverse-proxy boundary.
3. WEB6 candidate protected audit paths cannot be safely validated on Linux with Windows ACL checks; startup rejects those paths. Linux audit replacement is mandatory before WEB6 parity/release.
4. The ASP.NET Core host does not yet provide SPA static file hosting or operational HTTPS certificate provisioning.
5. The default repository `global.json` remains .NET 8 for preserved Windows net48 workflows; only the nested host pins .NET SDK 10.0.404. The `Vps04Server` build switch selects net10 for the shared Server source. Eight committed net10-specific NuGet locks (`packages.vps04a.lock.json`) are used with `--locked-mode` without modifying the existing net48 lock graph.
6. Compiled route reflection and successful compilation alone are not live Linux/SQL security or concurrency evidence.

## Next gates

Run independent live Linux HTTPS/API/SQL golden-master and security/idempotency tests against an approved SQL instance, covering the 178 frozen WEB2-WEB6 contract fixtures; independently review final HEAD and its Windows + Linux workflows before VPS-04A closeout. VPS-04B replaces the compatibility facade and verifies middleware, portable audit, real Linux runtime and deployment trust.
