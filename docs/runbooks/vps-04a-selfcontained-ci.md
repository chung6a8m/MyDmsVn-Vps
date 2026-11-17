# VPS-04A — Self-contained GitHub Actions Linux HTTPS → SQL13 acceptance

**Status (2026-11-17):** CI implementation and first SQL-backed execution **PASS** at code HEAD `30dddbcc8295f790f7e7df848e1442e0480d41cf`; [GitHub Actions #44064168553](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44064168553): **4/4 real Kestrel HTTPS/SQL tests**, zero failed/skipped; DbUp **13/13**, restricted runtime and read-only SQL oracle. Subsequent changes must rerun all required checks on the exact final PR HEAD. PR #9 remains **DRAFT / NOT MERGE APPROVED** pending independent review and outstanding acceptance scope.

## Three independent gates

| Gate | Workflow | Credentials and database | Meaning |
| --- | --- | --- | --- |
| Always-on source/NET10 | `.github/workflows/vps-04a-linux.yml` | No SQL | SDK 10.0.404, locked packages, 363 application, 205 host/contract cases including 178 individual frozen file pins, 57 route/action signatures |
| **Automatic ephemeral SQL acceptance** | **`.github/workflows/vps-04a-ci-sql.yml`** | Per-job generated; fresh SQL 2022 Developer Docker database, loopback-only SQL, no external DB or GitHub Environment secrets | Actual .NET10 Kestrel HTTPS + real SQL bootstrap, JWT/refresh/logout/replay, user authorization, transactional same-key create/idempotency/concurrency, failure no-write and SQL row/session/journal reconciliation |
| Protected staging/production-like | `.github/workflows/vps-04a-live-acceptance.yml`, `vps04a-sql-nonproduction` Environment, reviewed label | Operator-owned external isolated SQL13, protected GitHub Environment secrets, independent reviewer | Separate manual/approved gate; **NOT RUN** and not replaced by automatic CI |

**178 corpus files are not 178 HTTP request/response replays.** The existing test suite verifies each frozen Git blob and JSON grammar where applicable and WEB4 source fingerprint vectors. Full server wire parity with dynamic identities, SQL injected-failure rollback, stock/COGS oracle, protected WEB6 audit, reverse proxy, systemd and release approval remain separate gates.

## How automatic SQL CI works

The workflow runs on Ubuntu 24.04 for `pull_request` open/reopen/synchronize, `main` push and optional manual dispatch. No repository or Environment secrets are read.

1. Checks out the PR under read-only `contents:read`, no persisted credentials, and uses the nested .NET **10.0.404** SDK in `src/MyDmsVn.Server.AspNetCoreHost`. Legacy root `global.json` and Windows `net48` graph remain unchanged.
2. Generates unique strong per-job SA/runtime/oracle/writer/denied passwords and a JWT signing key. Values are immediately masked; kept only in the job environment, never committed, uploaded or printed. SQL port **1433 binds only to 127.0.0.1**. The SQL container has no persistent database volume and is removed with `docker rm -f -v` even if tests fail.
3. Runs Microsoft SQL Server **2022 Developer** container with forced TLS 1.2. Creates two ephemeral localhost-SAN certificates: one for SQL Server and one for Kestrel, locally trusted only within the disposable GitHub runner. The SQL Server account must read its private key (mode 0400, UID 10001) from an accessible container directory; the job explicitly checks this. No `-k`, `Encrypt=False`, or `TrustServerCertificate=True` is accepted.
4. Compiles **one CI-only .NET10 tool** `tests/MyDmsVn.Vps04a.SqlBootstrap` from the **original DbUp `DatabaseMigrationRunner`, migration lock/maintenance validator and unchanged SQL scripts 001–013**. It runs only on `GITHUB_ACTIONS=true` with explicit `VPS04A_CI_EPHEMERAL_SQL=approved-container-only`, exact loopback SQL SA connection and a fresh fixed-name database `MyDmsVnVps_Ci`. It refuses an existing database, wrong SQL endpoint or TLS trust bypass.
5. Verifies **13/13 original migrations, exact schema13 compatibility fingerprint**, seeds fresh active users and Customer/Warehouse/Employee/Product, provisions **restricted Web+Desktop runtime** using the reviewed `deployment/sql/Provision-MyDmsVnWebPrincipals.sql` and `Provision-MyDmsVnDesktopApiGrants.sql`, and independently grants Oracle **only SELECT** on WebAuthSessions, WebOperationJournal, SalesReceipts and SalesReceiptLines.
6. Re-runs the **production** runtime-principal and superuser binding validators and asserts Oracle has neither broad privilege nor write rights. Connection strings for **only the disposable local SQL instance** and generated business seed IDs flow via the protected job-local `GITHUB_ENV` file. `VPS04A_RUNTIME_SQL` and `VPS04A_ORACLE_SQL` are **not needed** in CI; keep these names exclusively for protected staging.
7. Builds the production .NET10 host and opt-in live tests with `-p:Vps04ALive=true`; launches a **real Kestrel HTTPS process**, verifies TLS/health without bypass, then requires exactly **4/4** test executions with no failed or skipped tests:
   - Login/JWT signature metadata, refresh rotation/replay revocation, logout, independent SQL session revocation.
   - Three simultaneous Sales.Create requests with **one same idempotency key**; identical results, **one** persisted journal + Sale Draft + line, same-key replay, changed-body key conflict and status/fingerprint reconciliation.
   - Invalid-body and stale-deployment-epoch denials with **zero** journal and business mutations.
   - Authenticated denied actor's SQL-backed session returns `403` on Sales.List.
8. Uploads only the test `TRX` artifact (no connection strings, passwords, TLS key files, SQL or Kestrel logs), then removes container, temporary cert/key files and local CA copies. A failed bootstrap fails the job; **no skip means no PASS**.

### SQL Server 2022 metadata permission compatibility

Initial SQL13 preflight denied runtime under SQL2022 with `Security.WebRuntimePrincipalOverprivileged`. The independent diagnostic identified **`VIEW SECURITY DEFINITION`**, which (with `VIEW PERFORMANCE DEFINITION`) SQL2022 exposes as a narrower permission **implied by required `VIEW DEFINITION`**. The Web runtime elevated-permission query was revised to admit only these two implied metadata rights, retaining rejection of DML, schema privileges, admin roles, broad EXECUTE and grant option. No additional GRANT is issued. The original SQL2017/Windows graph and schema scripts 001–013 were not altered.

### CI history and evidence

- [Failed CI startup](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44060400519): temporary TLS key file was chmod'd after ownership switched to SQL UID. Corrected ownership operation.
- [Failed NET10 bootstrap restore](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44060505090): root selected SDK8. Restores/builds now run from pinned nested SDK10.
- [Failed CI-only package restore](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44060797312): SQL Client7 demanded DI abstractions9, while legacy package pin8 is immutable. Fixed with test-bootstrap-only package override.
- [Failed SQL readiness](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44062544928): SQL UID could not open TLS key at prior private path; moved to a readable directory without relaxing file mode.
- [Failed runtime permission admission](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44063878274): SQL2022 implied metadata grants were not in older security allowlist; corrected narrowly, no privilege broadening.
- [First **PASS** Linux SQL E2E](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44064168553) at `30dddbcc8295f790f7e7df848e1442e0480d41cf`: **13/13 migration, 4/4 HTTPS→SQL test executions, no skips/failures**, final job cleanup PASS; TRX artifact `vps04a-selfcontained-https-sql-trx`, ID `14319322447`. Source/route Linux workflow [#44064168548](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44064168548) also PASS at this code commit.

Every code/documentation edit after the first PASS requires new exact-HEAD CI before claiming completion. These results establish **bounded nonproduction synthetic verification only**; do not conflate a container with deployment on the Owner's VPS or a managed staging environment.

## Operator commands

No GitHub Environment configuration is needed to run automatic SQL CI: open or synchronize PR #9 and inspect [Actions runs](https://github.com/chung6a8m/MyDmsVn-Vps/actions/workflows/vps-04a-ci-sql.yml). Inspect `vps04a-selfcontained-https-sql-trx` against the run's exact SHA and ensure `total=executed=passed=4`, `failed=notExecuted=0`.

Staging remains optional but strictly independent; follow [protected live SQL runbook](vps-04a-live-acceptance.md) if a separately approved persistent SQL staging environment is provisioned. **Do not** add `vps04a-live-approved` merely because automatic CI is green.

**Merge decision:** Keep PR #9 Draft until independent final-HEAD review, all required code CI and declared increment-specific acceptance gates have been evaluated; no production approval is implied by this SQL test.
