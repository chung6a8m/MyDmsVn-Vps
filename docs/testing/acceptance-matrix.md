# Acceptance matrix — independent quality gates

Legend: `PASS` = verified evidence at recorded SHA/environment; `NOT RUN` = no execution; `OPEN` = work required; `N/A` = outside current increment. Previous repo results are never imported.

| Scope | Current evidence / status | Later exit gate | Required evidence |
| --- | --- | --- | --- |
| Source import identity | PASS (VPS-00A only) | Preserve source attribution | `docs/import/README.md` and checksum manifest |
| Local ProjectReferences, no submodules | PASS at `72ceef5` (run #3) (static) | Every future PR | `scripts/verify-vps-foundation.py` report and GitHub Actions log |
| Windows Desktop net48 build | PASS at `72ceef5` (run #3) | VPS-03 Windows E2E | Locked restore, release build, Windows runner and SHA |
| Windows legacy HttpHost + DbMigrator build | PASS at `72ceef5` (run #3) | VPS-04/07 replaced | Target-specific locked restore/build and SHA |
| Existing SQL-free xUnit v2 smoke | PASS (VPS-00B baseline at prior SHA only) | VPS-01 v3 smoke: four strict TRX floors 13/212/363/640 | Final-HEAD Windows 2022 run, count per test assembly, zero skipped/failed |
| VPS-01 v3 discovery in all 12 suites | CONDITIONAL — requires successful final-HEAD CI and merge of PR #2 (see PR evidence) | VPS-01 | Four executed SQL-free suites and eight build/discover-only suites; nonempty floor per assembly |
| VPS-01 source HTTP route inventory | CONDITIONAL — frozen 29 signatures subject to PR #2 final-HEAD CI; live runtime NOT RUN | VPS-01 (source) / VPS-02 (runtime) | Reflection route signatures, immutable 178 fixture corpus, status/JSON envelope snapshots |
| SQL integration/concurrency | NOT RUN | VPS-07/09 | Real isolated SQL instance, outcomes + rollback data |
| GUI attended Windows | NOT RUN | VPS-03/09 | Attended UI interaction evidence on Windows |
| API and error/permission parity | IN PROGRESS — 46/46 source mapping, 28 added routes and new OWIN hosted test source; final-head CI and real SQL NOT YET VERIFIED | VPS-02 | [PR #3](https://github.com/chung6a8m/MyDmsVn-Vps/pull/3), [evidence](../evidence/vps-02-http-api-parity.md) and restricted SQL mutation verification |
| Desktop HTTP cutover | NOT RUN | VPS-03 | No Server/SQL edges, end-to-end live flow |
| Server ASP.NET Core net10 on Linux | NOT RUN | VPS-04 | Linux publish/runtime, auth, health and audit |
| Newtonsoft to System.Text.Json | NOT RUN | VPS-05 | Frozen wire and fingerprint oracles |
| MediatR to Mediator 3.x | NOT RUN | VPS-06 | Handler generation, behavior order and rollback |
| DbUp to FluentMigrator | NOT RUN | VPS-07 | Fresh + approved existing real SQL migrations |
| VPS deploy hardening/recovery | NOT RUN | VPS-08 | Service/TLS/secret/restart/rollback drills |
| Integrated release approval | NOT RUN | VPS-09 | Full matrix and explicit Owner acceptance |

Those four successful VPS-00B checks apply to the named pre-review run #3 SHA. Review-fix commits require a new final-HEAD CI run; the definitive post-review SHA, CI URL and merge result are recorded in PR #1 to avoid a self-referential evidence commit. Entries marked OPEN may be updated only with final-head evidence, and `docs/roadmap/README.md` remains the sole live status authority. See [baseline evidence](../evidence/vps-00b-baseline.md).
