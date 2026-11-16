# VPS-01 — Test & Contract Foundation evidence ledger

**Repository:** `chung6a8m/MyDmsVn-Vps`. **Branch:** `codex/vps-01-test-contract-foundation`. **PR:** [#2](https://github.com/chung6a8m/MyDmsVn-Vps/pull/2) (draft while CI/review pending). **Base:** merged VPS-00B `main` SHA `4503ae39989bcbf45fb883e5d0fbab610d043408` on 2026-11-16.

## Independent baseline and package boundary

VPS-00B Windows-2022 evidence before this increment: four SQL-free suites totaling **1,228 passed** (13 Architecture, 212 Desktop.Session, 363 Application, 640 legacy HttpHost), no skipped cases. This is a **pre-change reference**, not a xUnit v3 PASS. All twelve original test suites target `net48`; CrashHost is a helper, not a thirteenth xUnit suite. SDK stays at `8.0.400`; .NET 10 migration belongs to VPS-04.

## Changes introduced in this PR

- All twelve test project files are executable `OutputType=Exe`, package `xunit.v3 3.2.2` with existing VSTest adapter `3.1.5` and test SDK `17.14.1`. Locks were regenerated once with Windows GitHub Actions; the temporary lock-writing workflow was removed after use.
- xUnit v3 updates to test output helper namespaces, `IAsyncLifetime` `ValueTask`, SQL fixture diagnostics and custom `ITraitAttribute.GetTraits()` category metadata; SQL-unavailable skip remains visible as **NOT RUN**, never PASS.
- Original 178 WEB2–WEB6 frozen test assets and checksum manifest unchanged; new [29-route inventory](../contracts/vps-01-route-inventory.json) captures current 9 controller declarations, admission classification and fixture group. This is **source-only**.
- New `Vps01ContractFreezeTests` pins route reflection, local fixture names, and representative exact Newtonsoft success/error JSON/status; no application behavior or database schema changed.
- Two independent CI workflows: foundation Windows smoke with **minimum floors** (13/212/363/640) and stricter TRX, plus eight Windows compile/discover-only suites and Linux topology checker.
- Test-only analyzer migration debt in `tests/Directory.Build.targets`: `xUnit1051` warns about tests intentionally passing independent cancellation tokens; `xUnit3003` requests optional source-metadata constructors on existing custom Fact/Theory subclasses. Excluded from **test assemblies only**, no production `src/` analyzer relaxation. Neither suppression waives a failed test.

## Early observed red CI and correction

- [CI run 43848840601](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43848840601) on intermediate `ea26e48`: original three Windows production net48 builds and Architecture smoke passed; other smoke suites **FAILED** to compile because xUnit v3's `xUnit1051` analyzer was promoted to error by existing warnings-as-errors.
- [CI run 43849209711](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43849209711) on intermediate `d14bd5a`: static topology **PASS**, but eight Windows compile/discovery jobs failed due `xUnit1051`, `xUnit3003` and one nullable warning in a Security test. These failures remain recorded. Temporary `xunit.v3` lock regeneration was successful but does **not** count as executed tests.
- Follow-up code scopes analyzer compatibility only to tests, fixes nullable, removes temporary write-enabled workflow and mandates new final-HEAD CI.

## VPS-01 acceptance checklist

| Requirement | State (update against final PR HEAD only) |
| --- | --- |
| Source/lock inventory — 12 net48 executable xUnit v3 test projects | IMPLEMENTED; static checker and Windows locked restore required |
| 29 declared source HTTP routes and local pinned corpus | IMPLEMENTED; static check, reflection and wire tests required |
| Windows four SQL-free smoke suites with exact TRX nonempty/floor/no-skip | PENDING final-head successful execution evidence |
| Windows eight other suites compiled and discovery nonempty | PENDING final-head successful discovery evidence; SQL/GUI execution excluded |
| End-to-end SQL, security/ledger database oracle | NOT RUN; isolated non-production SQL environment required in later increments |
| Attended native Windows desktop tests | NOT RUN; interactive Windows required |
| Independent PR review, findings fixed, CI on last PR HEAD, merge | PENDING |

The final SHA, each Actions run URL, discovered/executed/skipped case counts, independent review and merge outcome should be appended to the PR discussion/closeout after real results exist; do not invent them or create an endless evidence-only commit loop. **Production deployment is not an acceptance claim of VPS-01.**

Live status is owned only by [roadmap README](../roadmap/README.md).
