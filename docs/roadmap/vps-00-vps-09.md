# VPS-00..09 — incremental implementation roadmap

**Baseline:** One-time source import pinned in `docs/import/README.md`. **Owner decisions:** [OWN-VPS-01..03](../decisions/README.md), approved 2026-11-16. This is a plan, **not** a list of already implemented features. Scope is ~14–16 reviewable PRs after dependency analysis.

## Sequencing and rationale

`VPS-00 -> VPS-01 -> VPS-02 -> VPS-03 -> VPS-04 -> VPS-05 / VPS-06 / VPS-07 -> VPS-08 -> VPS-09`

Desktop net48 currently references Server net48 directly. Retargeting Server to net10.0 before HTTP cutover breaks Desktop and mixed test projects. Test and contract baseline precede API/server changes; Serializer/Mediator/migration switches are isolated to avoid conflated regressions.

| Increment | Scope / split | Exit criteria |
| --- | --- | --- |
| VPS-00A | Import pinned source, 13 SQL scripts, tests, vendor snapshot. Exclude AppCore, submodules, inherited active governance. | Immutable import manifest, zero missing relative project refs, independent parity verification. **Completed for static integrity only.** |
| VPS-00B | Fresh README, AGENTS, AI_CONTEXT, Owner ADRs, architecture, inventory, roadmap, test policy, CI and build runbook. | Static path guard, Windows Desktop + legacy Server + DbMigrator build evidence, known baseline failures documented honestly, Windows/Linux CI on final HEAD. |
| VPS-01 | xUnit v3 test executable/discovery migration; frozen HTTP and JSON corpus. | Windows test count/discovery, focused UI, server and contract tests. Real required SQL tests cannot silently skip. |
| VPS-02A | Catalog CRUD, lookup, filter, paging parity in legacy host. | Each Desktop catalog operation has an endpoint with positive/negative, auth and concurrency tests. |
| VPS-02B | Identity, security admin, Sales, Goods Receipt, Inventory and remaining feature parity. | Full feature matrix and explicit error/session semantics tested; no undescribed missing endpoint. |
| VPS-03A | net48 typed HTTP transport/session/token-refresh/error mapping/cancellation. | Real HTTP cases, token expiry/rotation/logout, failure mapping and safe retry policy. |
| VPS-03B | Replace Desktop feature Local composition, remove Server/SQL references in production. | Windows Desktop -> HTTPS -> host -> SQL E2E; no Local/SQL fallback or Server dependency. |
| VPS-04A | Server Domain/Application/Infrastructure net10.0, ASP.NET Core HttpHost port; SDK 10.0.404. | Linux restore/build, core API parity; test topology no Desktop mix. |
| VPS-04B | Middleware, DI scopes, authorization/session, portable audit and Linux hosting. | Real Linux runtime, no OWIN/System.Web/Windows ACL server dependency, security regression. |
| VPS-05 | Separate Desktop Newtonsoft from Server System.Text.Json, typed DTO/converters. | Golden-wire snapshots: null/optional, decimals, long integers, dates, strict validation, idempotency fingerprints. |
| VPS-06 | Replace MediatR with Mediator.Abstractions + Mediator.SourceGenerator 3.x. | All handlers generated/resolved; exception, authorization, validation order and transactions unchanged. |
| VPS-07A | Convert immutable historical SQL 001–013 to FluentMigrator versions. | Exact script compatibility, transaction/batch behavior, no double-apply. |
| VPS-07B | Journal bridge, schema fingerprint, supported upgrade-path matrix. | Real SQL fresh + approved existing states, concurrency/interruption/retry and backup/restore. |
| VPS-08 | systemd, reverse proxy, TLS, secrets, logging, health, deploy/rollback. | Linux staged deployment with real restart/recovery, proxy trust and SQL connectivity. |
| VPS-09 | Integrated Desktop Windows -> Linux HttpHost -> real SQL Server and closeout. [Operator runbook](../runbooks/vps-09-integrated-acceptance.md) and [14-gate registry](../testing/vps-09-gates.json) implement fail-closed evidence review. | Source/ledger, stock/COGS, auth, rollback, migration, recovery, audit, attended GUI, verified 57-route live SQL matrix and explicit signed Owner release decision; source-only tests cannot close the gate. |

## Implementation invariants

- Preserve business semantics: auth/permissions, SQL transactions, stock ledger, moving-average costing, idempotency and concurrency.
- Preserve wire contracts until compatibility tests prove intentional changes.
- Do not inherit previous repository's CI PASS, test exception, SQL compatibility, quality gate or production approval.
- Do not put Desktop runtime on Linux. Linux VPS hosts Server; SQL Server can run elsewhere.
- FluentMigrator uses a new journal/bridge only after schema fingerprints and historic DbUp journal validation. No default destructive Down.
- Scope external packages using version pins and locked restore; no sibling checkout as build input.

## Review and evidence

One narrow PR per listed increment (larger splits allowed when dependency analysis requires them). Review independently; fix findings and rerun on the final HEAD. Record exact test selection, discovered/executed/skipped counts, environment and CI URLs in `docs/evidence/`. Mark acceptance only when its gate is proven. For dependency/quality gates see [architecture](../architecture/dependency-boundaries.md) and [acceptance matrix](../testing/acceptance-matrix.md).
