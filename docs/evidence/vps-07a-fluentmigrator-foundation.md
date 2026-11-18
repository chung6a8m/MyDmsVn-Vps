# VPS-07A — immutable FluentMigrator SQL version foundation (pre-cutover)

**Date:** 2026-11-18 · **PR:** [#24](https://github.com/chung6a8m/MyDmsVn-Vps/pull/24) · **Base `main`:** `1c47b0d3b59acbbaf4df5a4dfd58e6571fbe5330`.

## Scope: implementation and frozen sources

- New `net10.0` versioned-migration library, FluentMigrator **8.0.1** and SDK **10.0.404**, with committed two NuGet locks and locked restore.
- **13/13** original SQL blobs referenced as embedded resources, not copied/rewritten. Their SHA-256 values are independently compared to `docs/import/sql-checksums.sha256`.
- Explicit versions `1..13` in import order; **28 nonempty SQL batches**, not 29: `009_Security_Permission_Import.sql` ends in `GO`. Python source oracle and compiled C# contract test agree.
- Every historical migration is forward-only, `Down()` throws `NotSupportedException`.
- Existing `net48` DbUp executable, `dbo.SchemaVersions`, SQL scripts and production Server source have **not** been changed. The new assembly is not registered with a database runtime.

## CI on code HEAD prior to evidence-file commit

Code HEAD: `dd043ec29a0da2599dea093c1d8b3204c4e1bb62`. **8/8 workflows completed/success** on that same HEAD:

| Gate | Run | Strict meaning |
| --- | --- | --- |
| VPS-07A new foundation | [#44251684771](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44251684771) | SDK10.0.404, 13/13 SHA, 13 versioned migrations, 28 batches, compiled .NET10 contract and 13 blocked `Down`, strict NuGet locked restore |
| Linux ASP.NET Core 10 and route regression | [#44251684785](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44251684785) | SUCCESS; existing production Host, not the FM runner |
| Disposable HTTPS → SQL13 | [#44251684856](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44251684856) | SUCCESS; original DbUp SQL13 bootstrap and bounded existing SQL regression, **not an FM SQL upgrade test** |
| Windows legacy net48 and source foundation | [#44251684784](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44251684784) | 8/8 jobs SUCCESS, includes native architecture allowlist and net48 DbUp build |
| Windows xUnit v3 discovery | [#44251684848](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44251684848) | 10/10 jobs SUCCESS; explicitly classified new contract executable as non-xUnit |
| Desktop published artifact | [#44251684862](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44251684862) | SUCCESS; dependency/publish scope, not attended GUI |
| Mediator 06A canary | [#44251687952](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44251687952) | SUCCESS; isolated generator regression |
| Mediator 06B production | [#44251684775](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44251684775) | SUCCESS; no generated-Mediator regression |

**Final SHA policy:** Because this evidence file is an additional commit, re-run all required workflows on the **new PR HEAD** and record those run IDs and final review status on PR #24. Earlier CI is not final-HEAD acceptance for a later commit.

## Findings resolved

1. Initial expected `GO+1` miscount incorrectly reported 29 instead of 28 batches; script 009 has trailing GO and does not emit an empty SQL batch. Corrected both independent source and compiled test oracle.
2. Added 2 new projects without frozen foundation/xUnit/architecture inventory; three old gates correctly failed and were fixed by explicit 54-project inventory, six non-xUnit helpers and FM's standalone policy. No test exclusion or weakened discovery floor.
3. NuGet locks were generated in the first successful compile run and committed. Subsequent compile uses `--locked-mode` and treats warnings as errors.

## Explicitly not verified / not released

- No write-capable FluentMigrator runner or production cutover in VPS-07A; no FM version journal or bridge.
- No FluentMigrator fresh SQL install, upgrade from schema9..13, FM journaling/transactions, process interruption/retry, concurrent migration or data-retention oracle.
- The existing `SchemaVersions` source history and SQL13 hash expectations remain untouched; this increment cannot authorize deploying a new FM journal until the runtime fingerprint impact is addressed.
- Protected VPS-08 staging and VPS-09 integrated production release remain **NOT VERIFIED**. There is no Owner release approval.

**Follow-up:** [VPS-07B Issue #25](https://github.com/chung6a8m/MyDmsVn-Vps/issues/25) scopes the write-capable standalone net10 runner, strict dual-journal bridge, SQL real-oracle matrix and operational admission. No assumption that this PR passes 07B.
