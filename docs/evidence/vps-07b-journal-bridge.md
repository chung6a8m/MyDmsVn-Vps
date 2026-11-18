# VPS-07B — FluentMigrator journal bridge & real SQL acceptance ledger

**Date:** 2026-11-18 · [Draft PR #26](https://github.com/chung6a8m/MyDmsVn-Vps/pull/26) · [Issue #25](https://github.com/chung6a8m/MyDmsVn-Vps/issues/25) · **Base `main`:** `1ab58a0f7537c6dbcf6d238d0c6f5c2b922708e0`.
**Status: IMPLEMENTED / CODE-HEAD CI VERIFIED / INDEPENDENT REVIEW AND FINAL-HEAD CI PENDING / DO NOT MERGE.** This document is not a release certificate.

## Implemented candidate

- .NET SDK **10.0.404**, `net10.0`, FluentMigrator 8.0.1 Runner.SqlServer, explicit `schema10/12/13`; committed NuGet lock and `--locked-mode`.
- Immutable SQL 001–013 (13 pinned SHA-256, 28 nonempty batches). Existing `net48` DbUp tool and SQL scripts remain unchanged.
- Dedicated CLI admission: non-system application database, verified SQL TLS, CONTROL, no silent fallback, session `sp_getapplock`, exact source schema9..13/journal and restricted maintenance/writer drain.
- Native fingerprint preserves original SQL definition hashes, attests separate FM journal table shape and rejects unknown objects. Dual-journal bridge stamps only already-applied scripts in one transaction; forward migration appends the legacy and FM versions together.
- Independent isolated SQL Server 2022 test executable using **original DbUp SQL** as oracle, plus source/ledger/stock/valuation, Identity, WEB sessions and WEB operation SQL rows, adversarial journals, injected batch faults, killed worker, backup/restore and safe retry. CI tests run only against owned throwaway `localhost,1433` SQL with verified certificate and no persistent Docker volume.

## Evidence with precise boundaries

| Gate | Verified result |
| --- | --- |
| Historical pre-cutover 07A | [PR #24](https://github.com/chung6a8m/MyDmsVn-Vps/pull/24) merged at `1ab58a0`; 13 source versions, 28 batches, 13 locked hashes (source-only) |
| Initial VPS-07B SQL matrix (earlier code HEAD `29960822`) | [Run #44265646887](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44265646887), SQL2022 validated TLS, **18/18 executed PASS**, zero failed/skipped; includes fresh13, exact DbUp9..13 upgrades, initial tampering/concurrency |
| Initial Windows architecture regression | Earlier [Run #44265646991](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44265646991) **FAILED**: uncommitted independent SQL oracle lock. Subsequently committed frozen lock. New final-HEAD Windows gate still required |
| Expanded VPS-07B SQL matrix | **47/47 executed PASS, 0 failed/skipped** in [SQL2022 verified TLS run #44272872019](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272872019), code HEAD `4c3c84429547c89be03df87d936edfecbacd73f8`: explicit targets, 9/10/11/12/13 upgrade, two real migrators, early/middle/late SQL fault, process kill, data-readiness, privilege/journal/unknown schemas, backup/checksum/restore/retry. Exact **new final-HEAD rerun remains pending** after this documentation/floor commit. |
| Full required Windows/net48, Linux/net10, source-lock, HTTP/SQL regressions and independent full-diff review | **9/9 workflows SUCCESS** on code HEAD `4c3c844`: [07B SQL #44272872019](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272872019), [legacy Windows #44272871981](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272871981), [Windows discovery #44272872015](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272872015), [Linux Host #44272872085](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272872085), [HTTPS→DbUp13 #44272872009](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272872009), [Desktop publish #44272871799](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272871799), [FM source #44272871803](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272871803), [Mediator 06A #44272872142](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272872142), [Mediator 06B #44272871998](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272871998). **Independent review and exact final-HEAD rerun are pending.** |
| Protected staging / VPS / production SQL upgrade | **NOT VERIFIED** (VPS-08 / VPS-09; no Owner release decision) |

Mandatory final verification includes: exact9..13 and empty source admission, explicit targets10/12/13, equality of both journals, independently compared native definition hashes and nonzero protected/business rows, SQL transaction rollback after early/middle/late failures, process kill inside active SQL and recovery, non-privileged principal, unknown/future/tampered history and arbitrary object denial, restricted maintenance/writer drain, real SQL2022 backup/checksum/restore, no SQL or data loss on retry, and strict final-HEAD CI. Any skipped or missing gate blocks merge.

**Review policy:** audit the entire [PR #26 diff](https://github.com/chung6a8m/MyDmsVn-Vps/pull/26/files), regenerate independent SQL oracles and inspect exact job logs, generated lock graph, counts, final HEAD and checksum comparison. Update this ledger and the PR with run IDs and dispositions before deciding review/merge. A log saying `SUCCESS` for compilation alone never means VPS-07B acceptance.
