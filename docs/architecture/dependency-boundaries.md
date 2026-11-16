# Dependency boundaries and transition policy

## Current, measured from imported project files

- Production `src/`: 11 C# projects; every project targets `net48`.
- `Desktop.App` explicitly references `Desktop.Infrastructure.Local`, `Desktop.WinForms` **and** `Server.Infrastructure`.
- `Desktop.Infrastructure.Local` references `Server.Application`.
- `Server.HttpHost` references `Contracts`, `Server.Application` and `Server.Infrastructure`; it is not the ASP.NET Core host.
- WinForms uses vendored `MyDmsVn.BootstrapSourceGrid` under `vendor/`, whose SourceGrid dependency is also vendored.
- Test projects contain cross-layer fixture dependencies. Do not retarget Server while these remain.
- No Git submodules. The VPS-00A independent source-integrity check established 44 local projects and zero unresolved relative ProjectReferences (see `docs/import/README.md`).

## Current mandatory baseline rules (VPS-00B)

- Each `.sln` and `.csproj` ProjectReference must point to an existing local file within this repository, not a sibling checkout or absolute location.
- No Git gitlink, `.gitmodules`, literal escaping/absolute MSBuild Import or HintPath, or required source fetch. Property-derived SDK/package imports remain subject to successful locked restore/build. NuGet packages may come from `NuGet.config`.
- Legacy Desktop -> Server edges are **recorded debt**, not yet a CI failure. This prevents false-green architecture claims as well as premature breaking changes.
- Do not change the Desktop, Server, vendor or SQL production implementation in documentation/build foundation.

## Target enforceable rules (activated incrementally)

| Consumer | Allowed target | Gate |
| --- | --- | --- |
| Desktop net48 | Desktop.Application, Desktop.Http, neutral Contracts, vendored Windows UI | VPS-03 |
| Desktop production | No Server.*, SQL provider, local persistence or direct database access | VPS-03 |
| Server.HttpHost net10.0 | Server.Application, Server.Infrastructure, Contracts | VPS-04 |
| Server.Application net10.0 | Server.Domain, Contracts, SharedKernel | VPS-04 |
| Server.Domain net10.0 | SharedKernel only, no HTTP/UI/SQL | VPS-04 |
| Server.Infrastructure net10.0 | Application/Domain, SQL and portable OS interfaces | VPS-04 |
| Server.DbMigrator net10.0 | FluentMigrator migrations, restricted database admin path | VPS-07 |
| Contracts | Neutral DTOs, no server runtime dependency | VPS-03/VPS-05 |

Avoid a premature move of `Contracts` to `netstandard2.0` until Newtonsoft converter behavior is inventoried and verified. Architecture tests may move in different phases; failures inherited from legacy source must be recorded, not suppressed.
