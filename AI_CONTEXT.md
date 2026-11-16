# MyDmsVn-Vps — fresh context

Repository: `chung6a8m/MyDmsVn-Vps`, default branch `main`. New Git history, one-time source import, no upstream synchronization. This file is only a navigation map; instructions live in `AGENTS.md`.

**Approved on 2026-11-16:** OWN-VPS-01 Linux VPS / ASP.NET Core 10; OWN-VPS-02 SQL Server / FluentMigrator supporting fresh + verified existing database; OWN-VPS-03 only Desktop net48 (exclude Desktop.AppCore).

**As imported (historical):** both Desktop and Server were `net48`, with an OWIN/Web API 2 HttpHost, DbUp migrator, MediatR/Newtonsoft on Server and a Local desktop adapter. **Current VPS-03 draft PR #6:** production Desktop.App net48 references the new HTTP adapter and WinForms, not Local/Server/SQL. Legacy Local remains for tests. Server is still net48 with OWIN/DbUp/MediatR/Newtonsoft. Windows builds use SDK 8.0.400; SDK 10.0.404 and Linux Server remain VPS-04. Vendor WinFormsKit/SourceGrid are in-repo source; the SourceGrid net48 runtime excludes SqlClient.

**Roadmap:** VPS-00A import; VPS-00B docs/build foundation; VPS-01 xUnit v3/contract fixtures; VPS-02 API parity; VPS-03 Desktop full HTTP cutover; VPS-04 .NET 10/Linux server; VPS-05 System.Text.Json; VPS-06 Mediator 3.x; VPS-07 FluentMigrator; VPS-08 deploy/harden; VPS-09 integrated acceptance.

Read in order: `docs/roadmap/README.md`, `docs/decisions/README.md`, `docs/architecture/overview.md`, `docs/build/source-inventory.md`, `docs/testing/test-policy.md`, `docs/evidence/vps-00b-baseline.md` and `docs/import/README.md`.

**Strict evidence rules:** original-repo tests/approvals do not transfer. Static integrity is not a build. Windows net48 build is not Linux runtime. A missing SQL environment is NOT RUN, not PASS. Only roadmap README tracks live status.

**VPS-03 verification and blockers:** Draft PR #6; [evidence](docs/evidence/vps-03-desktop-http-cutover.md) records Windows 18/18 CI jobs at implementation HEAD including 1,286/1,286 SQL-free tests and 35-file x86 binary audit. [Issue #7](https://github.com/chung6a8m/MyDmsVn-Vps/issues/7) owns restart-safe unknown write recovery; [Issue #8](https://github.com/chung6a8m/MyDmsVn-Vps/issues/8) owns real Desktop HTTPS/SQL and attended GUI acceptance. Neither is currently approved as PASS. Roadmap README retains authority.
