# MyDmsVn-Vps — execution status (single source of truth)

**Owner decisions:** OWN-VPS-01, OWN-VPS-02 and OWN-VPS-03 APPROVED on 2026-11-16. See [decision register](../decisions/README.md). State below is for this repository **only**, never inherited from the source.

| Increment | Current branch status | Evidence/next gate |
| --- | --- | --- |
| VPS-00A — One-time Source Import | COMPLETE (static integrity only) | [2,282/2,282 source parity](../import/README.md), import verification SHA in import report |
| VPS-00B — Documentation & Build Foundation | COMPLETE UPON PR #1 MERGE, subject to passing CI on the final review-fix HEAD | [evidence ledger](../evidence/vps-00b-baseline.md): pre-review run #3, 8/8 jobs and 1,228/1,228 tests at `72ceef5`; final post-review SHA/CI and merge evidence in [PR #1](https://github.com/chung6a8m/MyDmsVn-Vps/pull/1) |
| VPS-01 — Test & Contract Foundation | COMPLETE UPON [PR #2](https://github.com/chung6a8m/MyDmsVn-Vps/pull/2) MERGE, conditional on independent review and final-HEAD CI PASS | [VPS-01 evidence](../evidence/vps-01-foundation.md): 12 net48 xUnit v3 test projects, 29 frozen source routes and Windows test-discovery/CI gates; see PR #2 for final SHA and run URLs |
| VPS-02 — HTTP API Parity | COMPLETE UPON [PR #5](https://github.com/chung6a8m/MyDmsVn-Vps/pull/5) REVIEW, FINAL-HEAD CI PASS AND MERGE | [SQL acceptance evidence](../evidence/vps-02-http-api-parity.md) at `91bab7d`: restricted principal 7/7, SQL HTTP exact11/12/13 7/7, packaged IIS HTTPS enabled/disabled 2/2 and HttpHost regression 683/683, zero skipped. This increment completion is not production enablement or VPS-03 cutover. |
| VPS-03 — Desktop HTTP Cutover | IN PROGRESS — implementation PR pending independent verification | [VPS-03 working evidence](../evidence/vps-03-desktop-http-cutover.md); no Windows/SQL E2E acceptance yet |
| VPS-04 — .NET 10 Server Replatform | NOT STARTED | ASP.NET Core 10 on actual Linux, SDK 10.0.404 |
| VPS-05 — System.Text.Json Migration | NOT STARTED | JSON fidelity/fingerprint regression |
| VPS-06 — Mediator 3.x | NOT STARTED | Generator, pipeline/permission/transaction parity |
| VPS-07 — FluentMigrator Transition | NOT STARTED | Fresh + legitimate existing SQL schema paths |
| VPS-08 — Linux Deployment & Hardening | NOT STARTED | systemd/reverse proxy/TLS/recovery |
| VPS-09 — Integrated Acceptance & Closeout | NOT STARTED | Windows Desktop + Linux Host + real SQL, signed acceptance |

## Status rules

- `NOT STARTED` means no implementation or acceptance evidence.
- `IN PROGRESS` means work exists; no acceptance implied. `IMPLEMENTED / CI VERIFIED` means code and specified CI gates passed on a named commit; review/merge and any new final-HEAD rerun remain separate.
- `COMPLETE` requires all documented **increment-specific** gates with commit-specific evidence; it does not imply production readiness.
- `BLOCKED` requires precise failed gate, environment, owner and next action. `EXCEPTION` requires explicit dated Owner acceptance; never infer one.
- Link PRs, HEAD SHA, CI runs, test counts, and any skipped/failed obligations under `docs/evidence/`. Distinguish Windows build, Linux static validation, real Linux runtime, SQL integration and attended GUI evidence.

Details, sequencing and each workstream's exit criteria live in [VPS-00..09 implementation roadmap](vps-00-vps-09.md). This README alone owns **live** status; do not create a competing status file.
