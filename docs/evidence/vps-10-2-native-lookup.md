# VPS-10.2 — Native catalog lookup pilot (draft implementation)

- Base main: `a20ce5f3114ddba990ec0f3a097a7907eea83116` (PR #34 merged).
- Four Catalog lookup actions use native MVC typed binding and STJ envelopes; remaining 53 routes retain bridge ownership. Inventory reads actual EndpointDataSource; compatibility controller/action identity stays separate.
- Additive Desktop metadata gate runs after admission, before MVC model binding. Existing lookups remain available with Desktop API disabled.
- OpenAPI v1-native is Development-only, native-only, SQL-independent at document generation. No production documentation endpoint. Schemas are derived from typed DTO/response metadata.
- 33 focused cases: 23 native Kestrel lookup cases, 6 mixed ownership cases, 4 OpenAPI/feature-gate Kestrel cases. New CI TRX requires all 33 executed/passed with zero skip; existing characterization, frozen differential, composition and SQL gates remain intact.
- No Desktop, SQL schema, Application handler, legacy net48 controller, or frozen oracle change.

## Package decision and provenance

Microsoft.AspNetCore.OpenApi 10.0.13 aligns with the existing net10.0.13 cryptography pins; Microsoft.OpenApi 2.12.2 is its transitive dependency. Host and test active lock snapshots have been prepared from matching package identity/version/dependency/contentHash metadata in independently checked public NuGet lock snapshots:
- https://github.com/fiso64/sockseek/blob/baaf223f6a95545640558531ca1ef835b5918374/Sockseek.Server/packages.lock.json
- https://github.com/ikemtz/NRSRx/blob/13ee282709e621b2656cbcbfe2e9cef46be1b57c/samples/IkeMtz.Samples.WebApi/packages.lock.json
- https://github.com/adr1enbe4udou1n/aspnetcore-realworld-example-app/blob/67507a3746e5c9c593f3b0df0db100ee8f842b39/src/Conduit.WebUI/packages.lock.json

These are metadata provenance only, not a build dependency, and **not locally generated or verified by NuGet restore**. Locked restore is mandatory; if graph verification fails, regenerate using the pinned SDK and inspect the delta before rerunning all required workflows.

## Verification boundary

This editing environment has no .NET SDK and cannot clone/restore via direct network. Source is submitted through GitHub API. Compile, targeted tests, live differential, Windows build and SQL gates are NOT RUN locally. Inspect the actual final PR HEAD's CI and any findings before independent review and merge.

Required next: successful locked restore, 33/33 new targeted cases, 37/37 characterization, 26/26 composition, 293/293 frozen live differential, 57 actual routes with exactly 4 native and 53 bridge, unchanged Windows and SQL gates, all 11 required workflows on final HEAD, independent review. VPS-08/09 protected deployment and production acceptance remain NOT VERIFIED. No merge or release is authorized by this draft.

## CI finding (initial source HEAD)

Initial HEAD `49af9d4eb24f02be5227813b23de253596ed7435` failed VPS-06A frozen lexical inventory: the net10-only native controller adds exactly 2 IMediator occurrences and 1 Send call. The legacy inventory must remain frozen at 171 sender references / 495 sends. Exclude only that exact new native controller source path, following the VPS-10.1 composition-test precedent; leave all expected legacy counts and production generated-Mediator verification unchanged. Source correction awaits final-HEAD CI. See https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45754613604.
