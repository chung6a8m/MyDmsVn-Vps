# VPS-07B — FluentMigrator journal bridge & real SQL acceptance ledger

**Date:** 2026-11-18 · [Draft PR #26](https://github.com/chung6a8m/MyDmsVn-Vps/pull/26) · [Issue #25](https://github.com/chung6a8m/MyDmsVn-Vps/issues/25) · **Base `main`:** `1ab58a0f7537c6dbcf6d238d0c6f5c2b922708e0`.
**Status: IMPLEMENTED / 54-case CODE-HEAD CI VERIFIED / EXACT FINAL-HEAD CI & INDEPENDENT REVIEW PENDING / DO NOT MERGE.** This document is not a release certificate.

## Implemented candidate

- .NET SDK **10.0.404**, `net10.0`, FluentMigrator 8.0.1 Runner.SqlServer, explicit `schema10/12/13`; committed NuGet lock and `--locked-mode`.
- Immutable SQL 001–013 (13 pinned SHA-256, 28 nonempty batches). Existing `net48` DbUp tool and SQL scripts remain unchanged.
- Dedicated CLI admission: non-system application database, verified SQL TLS, CONTROL, no silent fallback, session `sp_getapplock`, exact source schema9..13/journal and restricted maintenance/writer drain.
- Native fingerprint preserves original SQL definition hashes, attests separate FM journal table shape and rejects unknown objects. Dual-journal bridge stamps only already-applied scripts in one transaction; forward migration appends the legacy and FM versions together.
- Review hardening: database-scope extended-property marker `MyDmsVn.FluentMigratorBridge=vps07b-v1` is created **atomically** with the new FM table, and strict byte-length marker validation rejects loss/forgery. A previously cut-over database missing the FM journal is no longer accepted as an unbridged DbUp source. Direct PUBLIC grants on application-owned objects are rejected.
- A dedicated SQL login with database CONTROL, temporary restricted-user `db_owner` membership, and SQL2022 DMV visibility grants has successfully performed schema12→13 + no-op in isolated SQL2022 CI; the ordinary SQL Host principal is never used for DDL.
- Independent isolated SQL Server 2022 test executable using **original DbUp SQL** as oracle, plus source/ledger/stock/valuation, Identity, WEB sessions and WEB operation SQL rows, adversarial journals, injected batch faults, killed worker, backup/restore and safe retry. CI tests run only against owned throwaway `localhost,1433` SQL with verified certificate and no persistent Docker volume.

## Evidence with precise boundaries

| Gate | Verified result |
| --- | --- |
| Historical pre-cutover 07A | [PR #24](https://github.com/chung6a8m/MyDmsVn-Vps/pull/24) merged at `1ab58a0`; 13 source versions, 28 batches, 13 locked hashes (source-only) |
| Initial VPS-07B SQL matrix (earlier code HEAD `29960822`) | [Run #44265646887](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44265646887), SQL2022 validated TLS, **18/18 executed PASS**, zero failed/skipped; includes fresh13, exact DbUp9..13 upgrades, initial tampering/concurrency |
| Initial Windows architecture regression | Earlier [Run #44265646991](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44265646991) **FAILED**: uncommitted independent SQL oracle lock. Subsequently committed frozen lock. New final-HEAD Windows gate still required |
| Expanded VPS-07B SQL matrix | [Initial 47/47 run #44272872019](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44272872019) at `4c3c844`; [51/51 run #44281872100](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281872100) at `274a739` added durable missing/forged cutover marker refusal; [54/54 run #44283257703](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44283257703) at `1e75bd1` added positive dedicated SQL CONTROL login + public-grant refusal. **All 54 discovered/executed; 54 PASS, 0 failed, 0 skipped** in ephemeral SQL2022 with verified TLS. The code HEAD has since changed for stricter exact-refusal diagnostics; rerun on **final PR HEAD** is required. |
| Full required Windows/net48, Linux/net10, source-lock, HTTP/SQL regressions and independent full-diff review | **9/9 workflows SUCCESS** on previously verified 51-case code HEAD `274a739`: [07B #44281872100](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281872100), [legacy Windows #44281872149](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281872149), [xUnit discovery #44281872117](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281872117), [Linux Host #44281872148](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281872148), [HTTPS→SQL #44281872016](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281872016), [Desktop artifact #44281872029](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281872029), [FM source #44281872011](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281872011), [Mediator 06A #44281872044](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281872044), [Mediator 06B #44281876444](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44281876444). 54-case code HEAD `1e75bd1` had 54/54 SQL SUCCESS, but all nine final-HEAD regressions and independent review remain separate and required. |
| Protected staging / VPS / production SQL upgrade | **NOT VERIFIED** (VPS-08 / VPS-09; no Owner release decision) |

Mandatory final verification includes: exact9..13 and empty source admission, explicit targets10/12/13, equality of both journals, independently compared native definition hashes and nonzero protected/business rows, SQL transaction rollback after early/middle/late failures, process kill inside active SQL and recovery, non-privileged principal, unknown/future/tampered history and arbitrary object denial, restricted maintenance/writer drain, real SQL2022 backup/checksum/restore, no SQL or data loss on retry, and strict final-HEAD CI. Any skipped or missing gate blocks merge.

**Review policy:** audit the entire [PR #26 diff](https://github.com/chung6a8m/MyDmsVn-Vps/pull/26/files), regenerate independent SQL oracles and inspect exact job logs, generated lock graph, counts, final HEAD and checksum comparison. Update this ledger and the PR with run IDs and dispositions before deciding review/merge. A log saying `SUCCESS` for compilation alone never means VPS-07B acceptance.


## Independent review findings — code fixes committed, final-HEAD CI required

6. **P1 — inherited PUBLIC permission bypass.** A grant at `SCHEMA::dbo` or database scope could expose application objects without an object-level grant; refuse object, schema and broad database grants in preflight, with independent SQL negatives.
7. **P1 — bare legacy SQL alias provenance.** Enforce the complete original embedded DbUp resource name; add forged bare-alias refusal.
8. **P1 — interrupted fresh prefix not explicitly exercised.** Inject failure in SQL005 after versions1..4 commit, prove dual-journal consistency and fail-closed rerun; document operator-led fresh replacement/backup restore.

**New required acceptance floor: 58/58 real SQL cases**, zero failed/skipped, on exact new PR HEAD; earlier 54/54 is historical evidence only.

## Review findings addressed on this branch

1. **P0 — deleted FM journal could have been restamped as unbridged DbUp.** Fixed with a database-scoped durable cutover marker created in the same serializable transaction as the FM journal. Tested absent/forged marker and deleted journal on SQL2022; native fingerprint admission also verifies this binding. No original DbUp script or legacy journal row was changed.
2. **P1 — missing positive least-scope migrator identity.** Added real SQL2022 dedicated login with explicit database CONTROL, maintenance-only db_owner membership and DMV permissions; verified 12→13 upgrade and zero-replay no-op; no production credentials.
3. **P1 — unexpected PUBLIC grant on business object not rejected before migration.** Added fail-closed preflight and independently tested a forged `GRANT SELECT ON dbo.Users TO public`.
4. **P1 — negative SQL tests only checked generic nonzero exit.** Tightened specific refusal-code assertions and mapped malformed journal definition to a stable safe code. Exact final-HEAD CI pending.
5. **P1 — backup/restore caused transient DMV cleanup race.** Fixed via bounded re-execution of the original unchanged read-only writer-drain validator with the same exclusive app lock. No maintenance fallback, journal auto-fix or security bypass.

**The implementation author performed a source/diff sanity check, not an independent reviewer approval.** PR #26 must be independently reviewed and its exact final HEAD CI confirmed before merger. No production or protected PREPROD acceptance is implied.