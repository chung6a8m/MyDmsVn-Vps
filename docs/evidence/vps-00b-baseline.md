# VPS-00B — independent baseline evidence ledger

**Increment:** Documentation & Build Foundation. **Branch:** `codex/vps-00b-documentation-build-foundation`. **Initial base:** `086e8699797b05600ad5d7b256b4175e67e4c8c6` (2026-11-16). All CI results must reference the final PR HEAD, not a previous revision.

## Verified before this increment

- VPS-00A import verified **2,282/2,282** code/vendor copied files with identical blob/mode, no missing or unexpected source file; full static result recorded at `docs/import/README.md`.
- Source on base has 44 C# project files (11 src, 13 tests including crash host, 4 samples, 3 tools, 13 vendor); 13 original migration SQL scripts.
- Base production `src/` is entirely `net48`. Desktop references Server Infrastructure; no CI workflow is present. Those are *inventory findings*, not a test failure.

## VPS-00B verification checklist

| Gate | Initial state | Evidence required before completing VPS-00B |
| --- | --- | --- |
| Root docs, three Owner decisions and independent roadmap | IMPLEMENTED on branch | PR diff and cross-links reviewed |
| Static repo boundary guard | NOT RUN at initial commit | Linux CI success + JSON checker report on HEAD |
| Desktop.App net48 Windows build | NOT RUN at initial commit | Windows locked restore/build job on HEAD |
| Legacy HttpHost net48 Windows build | NOT RUN at initial commit | Windows locked restore/build job on HEAD |
| Legacy DbMigrator net48 Windows build | NOT RUN at initial commit | Windows locked restore/build job on HEAD |
| SQL-free smoke tests (nonempty count) | NOT RUN at initial commit | Windows TRX artifact / executed-count guard on HEAD |
| SQL Server integration | NOT RUN, excluded from VPS-00B CI | Separate non-production SQL instance needed |
| Windows attended UI | NOT RUN, excluded from headless CI | Later attended test protocol |
| ASP.NET Core 10 running on Linux | NOT RUN, not in VPS-00B | VPS-04/VPS-09 |

If CI identifies a pre-existing issue, record the job, exact failing step, first failure, scope and follow-up. Do not mark any red or absent gate PASS. Do not use upstream tests/production approvals. The task-status authority is [docs/roadmap/README.md](../roadmap/README.md); CI URL and acceptance should be added to PR review/closeout on the verified HEAD.
