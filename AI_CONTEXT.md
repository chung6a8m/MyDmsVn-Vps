# MyDmsVn-Vps — fresh context

Repository: `chung6a8m/MyDmsVn-Vps`, default branch `main`. New Git history, one-time source import, no upstream synchronization. This file is only a navigation map; instructions live in `AGENTS.md`.

**Approved on 2026-11-16:** OWN-VPS-01 Linux VPS / ASP.NET Core 10; OWN-VPS-02 SQL Server / FluentMigrator supporting fresh + verified existing database; OWN-VPS-03 only Desktop net48 (exclude Desktop.AppCore).

**As imported (historical):** both Desktop and Server were `net48`, with an OWIN/Web API 2 HttpHost, DbUp migrator, MediatR/Newtonsoft on Server and a Local desktop adapter. **Current VPS-03 PR #6 (review/merge gate):** production Desktop.App net48 references the new HTTP adapter and WinForms, not Local/Server/SQL. Legacy Local remains for tests. Server is still net48 with OWIN/DbUp/MediatR/Newtonsoft. Windows builds use SDK 8.0.400; SDK 10.0.404 and Linux Server remain VPS-04. Vendor WinFormsKit/SourceGrid are in-repo source; the SourceGrid net48 runtime excludes SqlClient.

**Roadmap:** VPS-00A import; VPS-00B docs/build foundation; VPS-01 xUnit v3/contract fixtures; VPS-02 API parity; VPS-03 Desktop full HTTP cutover; VPS-04 .NET 10/Linux server; VPS-05 System.Text.Json; VPS-06 Mediator 3.x; VPS-07 FluentMigrator; VPS-08 deploy/harden; VPS-09 integrated acceptance.

Read in order: `docs/roadmap/README.md`, `docs/decisions/README.md`, `docs/architecture/overview.md`, `docs/build/source-inventory.md`, `docs/testing/test-policy.md`, `docs/evidence/vps-00b-baseline.md` and `docs/import/README.md`.

**Strict evidence rules:** original-repo tests/approvals do not transfer. Static integrity is not a build. Windows net48 build is not Linux runtime. A missing SQL environment is NOT RUN, not PASS. Only roadmap README tracks live status.

**VPS-03 verification and blockers:** [PR #6](https://github.com/chung6a8m/MyDmsVn-Vps/pull/6) contains the production Desktop HTTP cutover. [Issue #8](https://github.com/chung6a8m/MyDmsVn-Vps/issues/8) is closed with Windows/HTTPS/SQL 46/46 method parity, 86/86 native core and 1/1 attended GUI on pinned SHA `4c0b1be`. [Issue #7](https://github.com/chung6a8m/MyDmsVn-Vps/issues/7) is closed with 15/15 crash/recovery/authorization/attended/cleanup gates on pinned SHA `6baa1e8`. [Evidence](docs/evidence/vps-03-desktop-http-cutover.md) and [Issue #7 closeout](docs/evidence/vps-03-issue-7-closeout.md) distinguish those packages from later documentation commits. Independent review, CI on the final review-fix HEAD and PR merge are separate VPS-03 completion gates. No Linux runtime, VPS-09 or production PASS is implied. Roadmap README retains authority.
