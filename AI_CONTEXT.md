# MyDmsVn-Vps — fresh context

Repository: `chung6a8m/MyDmsVn-Vps`, default branch `main`. New Git history, one-time source import, no upstream synchronization. This file is only a navigation map; instructions live in `AGENTS.md`.

**Approved on 2026-11-16:** OWN-VPS-01 Linux VPS / ASP.NET Core 10; OWN-VPS-02 SQL Server / FluentMigrator supporting fresh + verified existing database; OWN-VPS-03 only Desktop net48 (exclude Desktop.AppCore).

**As imported:** both Desktop and Server are `net48`. HttpHost is OWIN/Web API 2; Database migrator uses DbUp; Server/contract code uses MediatR and Newtonsoft.Json; Desktop has Local adapter + references Server. Windows builds use SDK 8.0.400 per `global.json`. The future SDK 10.0.404 is planned for VPS-04, not present now. Vendor WinFormsKit and SourceGrid are local source.

**Roadmap:** VPS-00A import; VPS-00B docs/build foundation; VPS-01 xUnit v3/contract fixtures; VPS-02 API parity; VPS-03 Desktop full HTTP cutover; VPS-04 .NET 10/Linux server; VPS-05 System.Text.Json; VPS-06 Mediator 3.x; VPS-07 FluentMigrator; VPS-08 deploy/harden; VPS-09 integrated acceptance.

Read in order: `docs/roadmap/README.md`, `docs/decisions/README.md`, `docs/architecture/overview.md`, `docs/build/source-inventory.md`, `docs/testing/test-policy.md`, `docs/evidence/vps-00b-baseline.md` and `docs/import/README.md`.

**Strict evidence rules:** original-repo tests/approvals do not transfer. Static integrity is not a build. Windows net48 build is not Linux runtime. A missing SQL environment is NOT RUN, not PASS. Only roadmap README tracks live status.
