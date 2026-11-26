# Architecture — current and target

## Source baseline (VPS-00A/VPS-00B)

`Desktop.App (net48) -> Desktop.Infrastructure.Local (net48) -> Server.Application/Infrastructure (net48) -> SQL Server`

A separate legacy `Server.HttpHost (net48, Web API 2/OWIN)` provides an incomplete HTTP facade. `Server.DbMigrator (net48)` uses DbUp and embedded SQL scripts. `Contracts` are currently `net48` and coupled to Newtonsoft attributes. These are factual legacy boundaries, **not** an endorsement of the final topology.

## Original approved destination (VPS-00 planning)

`WinForms Desktop net48 -> HTTPS/JSON -> ASP.NET Core 10 HttpHost on Linux VPS -> Server Application/Domain/Infrastructure net10.0 -> SQL Server`

Future migrations run through an independent FluentMigrator entry point. Desktop should not access SQL or reference Server assemblies. Shared contracts should become framework-neutral only after serializer-compatibility and project reference tests.

## Required transitions

1. VPS-01: build test safety net and frozen HTTP/JSON contract corpus.
2. VPS-02: supply full feature/API parity while retaining legacy host to isolate HTTP behavior changes.
3. VPS-03: switch Desktop production composition from Local to HTTP, including authorization/session/error mapping; eliminate direct Server/SQL dependency.
4. VPS-04: then migrate Server to `net10.0` and Linux; SDK target `10.0.404`; eliminate OWIN/System.Web/server Windows-only dependencies.
5. VPS-05/06/07: isolate serialization, Mediator and migration changes with independent regression gates.
6. VPS-08/09: hosting hardening and end-to-end sign-off.

## Invariants

Authentication, role/permission enforcement, SQL transaction boundaries, stock ledger and moving-average valuation, idempotency/fingerprint, concurrency, wire JSON and database schema protection cannot silently change. Any intentional contract change needs a versioning and acceptance record. See [dependency boundaries](dependency-boundaries.md) and [test matrix](../testing/acceptance-matrix.md).

## Post-migration target — VPS-10 (approved 2026-11-26)

The historical baseline and transition sequence above are not a current runtime inventory. At the VPS-10 planning source `6a9649430cfd8ff4576d5e351f4ef6d0080011a0`, the production `Server.AspNetCoreHost` already targets `net10.0` and uses Kestrel, System.Text.Json and generated Mediator, while its HTTP surface still source-links the legacy controllers through a compatibility bridge.

[OWN-VPS-05](../decisions/OWN-VPS-05.md) approves replacing that bridge with native ASP.NET Core controllers, feature-owned typed HTTP DTOs and centralized invariant wire binding/serialization. Desktop remains `net48` and can be upgraded in coordination with the Server. Preserve all current WEB2–WEB6/Desktop contracts, including existing operation routes under `/api/v2` and `/api/v3`, and persisted operation/replay behavior.

See the [VPS-10 target design](native-aspnetcore10-host.md), [implementation plan](../roadmap/vps-10-native-aspnetcore-host.md) and [single live status index](../roadmap/README.md). Target architecture approval does not imply implementation or runtime acceptance.
