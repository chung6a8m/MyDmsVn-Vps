# VPS-10.2 — Native catalog lookup pilot (draft implementation)

- Base main: `a20ce5f3114ddba990ec0f3a097a7907eea83116` (PR #34 merged).
- Source preparation in this PR: native MVC controller/binder/JSON/result mapping for four Catalog lookup routes; remaining 53 bridge endpoints; actual EndpointDataSource inventory and mixed ownership checks; additive Desktop route metadata/gate; SQL-free live regression fixture uses the native mapping.
- No Desktop, SQL schema, Application handler, legacy net48 controller, or frozen oracle change.
- Native ApiExplorer metadata is present; the required pinned Microsoft.AspNetCore.OpenApi package, generated lock snapshots, document mapping, schema/runtime agreement tests, and focused pre-binding feature-gate HTTP tests are **outstanding**. This is deliberately a draft, not the full VPS-10.2 deliverable.

## Verification boundary

The available editing environment has no .NET SDK and cannot clone the repository over Git/restore from NuGet. The source is submitted via the GitHub API. No compile, targeted test, live differential, Windows build or SQL gate has been executed locally. All results remain NOT RUN locally; any CI results must be inspected against the actual PR HEAD.

Required next: complete OpenAPI using genuine restored package locks; run focused `Vps10NativeCatalogTests` and `Vps10EndpointInventoryTests` with the SDK and flags in the roadmap; verify G1/G2/G3, all 11 required workflows and independent review on the final HEAD. Keep existing 37 characterization, 293 frozen differential cases and SQL regression gates unchanged.

VPS-08/09 protected deployment and production acceptance remain NOT VERIFIED. No merge or release is authorized by this draft.
