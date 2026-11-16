# VPS-01 — Test & Contract Foundation evidence ledger

**Repository:** `chung6a8m/MyDmsVn-Vps`. **Branch:** `codex/vps-01-test-contract-foundation`. **PR:** [#2](https://github.com/chung6a8m/MyDmsVn-Vps/pull/2) (independent review and merge pending). **Base:** merged VPS-00B `main` SHA `4503ae39989bcbf45fb883e5d0fbab610d043408` on 2026-11-16.

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

## Verified intermediate Windows results — SHA 751b8f1

[Foundation workflow 43850012731](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43850012731) completed **8/8 jobs SUCCESS** at `751b8f1`: static Linux, three net48 Windows builds, and TRX-backed Architecture **13/13**, Desktop.Session **212/212**, Application **363/363**, HttpHost **648/648**. Combined **1,236 passed, 0 failed, 0 skipped** (new contract tests contribute eight).

[VPS-01 workflow 43850012712](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43850012712) completed **9/9 jobs SUCCESS** at `751b8f1`: Linux project topology/route/fixture static checks and eight Windows locked build and test discovery jobs. **3,255 discovered, not executed**: Desktop 813, Desktop.Ui 382, Catalog 17, Foundation 66, IntegrationTests 59, Inventory 127, Migrations 107, Security 1,684. These intermediate results do not automatically attest later commits.

## Independent review corrections — 2026-11-16

- **P1, fixed:** the HttpHost `minimum_tests: 640` requirement was in the Windows production-build matrix rather than the four-suite `windows-smoke` matrix. It is now attached to the executed HttpHost suite; the Linux static checker additionally validates all four numeric smoke floors in the correct job.
- **P2, fixed:** the acceptance matrix contained literal backslash-n row separators, now real Markdown row breaks.
- **P2, fixed:** the route reflection test previously used a hard-coded list of nine controllers. It now discovers attribute-route controllers from the host assembly and requires exactly nine at the frozen baseline, detecting future additions as well as edits/removals.

The review corrections require both GitHub Actions workflows to run again on their resulting final PR HEAD. See the PR discussion for actual SHA, CI results and merge outcome; do not treat intermediate runs as sufficient.

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
