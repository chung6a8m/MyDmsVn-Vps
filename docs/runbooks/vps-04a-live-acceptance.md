# VPS-04A — Live Linux HTTPS → isolated SQL Server acceptance

**Increment:** VPS-04A / PR #9. **Gate owner:** independent acceptance operator.
**Disposition as of 2026-11-17 (protected staging lane ONLY):** this separately approved staging live run remains **NOT RUN / NOT VERIFIED** until its protected, isolated SQL13 environment is supplied and its final-HEAD Actions run passes. **Automatic disposable SQL Server CI is separately VERIFIED 4/4 at code HEAD `30dddbcc`**; see [self-contained CI runbook](vps-04a-selfcontained-ci.md). This file does not approve release, production SQL access, VPS-04B, VPS-05, or VPS-07.

## What the two acceptance lanes actually establish

| Lane | Execution | Oracle | Disposition |
| --- | --- | --- | --- |
| Always-on Linux .NET SDK 10.0.404 | `.github/workflows/vps-04a-linux.yml` | 363 Application regression tests, 25 existing HTTP boundary tests, 57 compiled routes, **178 pinned corpus files** and frozen WEB4 canonical SHA-256 vectors | CI on final HEAD required |
| Explicit approved Linux HTTPS+SQL | `.github/workflows/vps-04a-live-acceptance.yml` | Four live acceptance facts, independent read-only SQL row/session/journal evidence | **NOT RUN** |
| WEB2–WEB6 full live JSON parity | Dedicated request/response corpus replay with SQL-controlled dynamic identities | Every applicable request/response fixture in context; reject illegal/future fields, strict JSON and numeric/date fidelity | **NOT VERIFIED**; the source corpus contains non-JSON documents, scripts, manifests and illustrative IDs |
| SQL fault injection/physical rollback, migration, Desktop/Linux cross-machine, WEB6 resend, reverse proxy and systemd | Later VPS-04B/VPS-07/VPS-08/VPS-09 and dedicated approved tests | Independent state and recovery oracles | **NOT VERIFIED** |

**Corpus terminology:** the historical `docs/contracts/corpus-blobs.sha1` pins **178 files** (34 WEB2 + 38 WEB3 + 48 WEB4 + 41 WEB5 + 17 WEB6), **not 178 HTTP JSON payloads**. Native `Vps04aFrozenCorpusTests` reads each checkout file, normalizes only Git `text eol` CRLF to LF for its **Git object hash** calculation, verifies original Git blob SHA-1, validates JSON grammar where applicable, rejects unpinned extra files and compares production NET10 `WebOperationWireInput.Canonicalize` output against independently authored WEB4 canonical/hash vectors. No frozen file, hash manifest or expectation was changed. This lane **does not** assert 178 live SQL responses or System.Text.Json parity (VPS-05).

## Nonproduction admission: fail closed

Execute only after an operator creates a **disposable, non-production SQL Server database** at **exact Web schema13**, records a backup/restore or disposal boundary, completes reviewed schema migration and principal provisioning, and approves mutation of **test records only**. Do not run against production data or an existing shared business database. The new workflow **never** migrates or drops SQL objects, seeds users, grants roles, rotates keys or destroys databases; it leaves its one committed Sale Draft for inspection and operator-owned cleanup. SQL setup stays with the separate migration identity; production Web principal never receives migration DDL.

1. Configure GitHub **Environment `vps04a-sql-nonproduction`** with mandatory reviewers and **only** the exact deployment ref `refs/pull/9/merge`, not the source branch name. GitHub uses the synthetic merge ref for `pull_request.labeled`; the workflow pins checkout to the reviewed `pull_request.head.sha` and rechecks **PR open/unmerged status, approval label, same repo/source/base branch and current HEAD** before and after SQL acceptance. Removing the label or closing/retargeting the PR while waiting for review invalidates the pending run. Ensure its runner can reach the approved isolated SQL Server over a validated encrypted connection; the application refuses incompatible schema, principal and superuser bindings. Record database fingerprint and version in private operator notes, not GitHub logs.
2. Provision a **restricted runtime SQL principal** with the exact schema13 WEB5 grants and Desktop API grants; a **separate read-only SQL oracle principal** must SELECT only `dbo.WebAuthSessions`, `dbo.WebOperationJournal`, `dbo.SalesReceipts`, and `dbo.SalesReceiptLines`. Never use the migration or server-admin principal for the host or oracle. Provision an active writer account with Sales Read/Write/Post and an active, independently denied account without Sales Read.
3. Seed **active test-owned** Customer, Warehouse, Employee, Product rows with valid references. Their integer IDs must be supplied as environment variables; illustrative IDs (101/102/103/104) in the WEB4 fixtures are **not** authoritative live SQL IDs. The writer must be authorized to create a Sale Draft.
4. Set these protected **Environment secrets** (redact their values): `VPS04A_RUNTIME_SQL`, `VPS04A_ORACLE_SQL`, `VPS04A_SUPERUSER_USERNAME`, `VPS04A_JWT_AUDIENCE`, `VPS04A_JWT_KID`, `VPS04A_JWT_KEYS`, `VPS04A_WRITER_USERNAME`, `VPS04A_WRITER_PASSWORD`, `VPS04A_DENIED_USERNAME`, `VPS04A_DENIED_PASSWORD`. Never commit credentials or send them in chat. The SQL connection strings must enforce encryption and verify the SQL server certificate/CA; use no `TrustServerCertificate=True` or `Encrypt=False`.
5. Set Environment **variables** `VPS04A_CUSTOMER_ID`, `VPS04A_WAREHOUSE_ID`, `VPS04A_EMPLOYEE_ID`, `VPS04A_PRODUCT_ID` to approved active fixture identities. Ensure the SQL URL is reachable and not exposed to the public internet.
6. **Before PR merge:** once the protected Environment is configured and its reviewers have approved the isolated SQL dataset, add label **`vps04a-live-approved`** to PR #9. The existing VPS-04A `pull_request.labeled` workflow invokes the branch-local reusable live workflow. Its guards require PR #9, the exact merge ref, the trigger label, same-repository source branch and target `main`. The workflow **checks out the event's reviewed head SHA rather than the synthetic merge commit**, then independently verifies the GitHub PR is still open, unmerged, labeled, on the same reviewed source/base branch and exact HEAD before SQL mutation and after execution. Ordinary PR synchronize/push and `workflow_dispatch` cannot execute this protected live job. After *any* code change, remove/reapply the label to rerun at the new exact HEAD. A changed HEAD disqualifies an in-flight run from final-HEAD evidence. The live job checks every setting, enforces explicit `web5` and `VPS04A_LIVE_ALLOW_MUTATION=approved-nonproduction-only`, and fails on refused startup, absent or skipped tests. `Vps04ALive` remains **off** in normal SQL-free CI.

## TLS and live oracle details

The runner launches the **actual published .NET10 Kestrel process** at `https://localhost:50443` using an ephemeral, localhost-SAN TLS certificate. Its certificate is installed into the *ephemeral runner trust store*; `curl` and the tests verify the normal server certificate chain/hostname. **No `-k`, certificate-validation bypass, OWIN TestServer or in-memory mock** is used. The host must connect using the restricted runtime identity to real remote/isolated SQL; SQL oracle reads run using separate credentials. The TLS certificate and private key are destroyed after the job; neither raw host logs nor secrets are uploaded.

The conditional NET10 class `Vps04aLiveSqlAcceptanceTests` requires **4/4** independent facts:

- **JWT/session:** HTTPS health; anonymous request 401, cross-origin 403; login, signed JWT algorithm/issuer/audience/session identity and secure `__Host-` cookie; actual authenticated `me`; refresh rotates JWT/cookie; replaying consumed refresh revokes family; logout revokes a separate session. Independently SELECT `WebAuthSessions.RevokedAtUtc` for both session IDs.
- **Transaction/idempotency/concurrency:** three simultaneous, same-key/same-body authenticated Sales.Create requests return the **same original response**, with exactly one `WebOperationJournal` row and exactly one Sale Draft and line in SQL. Same-key replay returns same original body; changed-body/same-key yields `Idempotency.KeyConflict` (409), and authenticated status with the **SQL-authoritative** request fingerprint recovers COMMITTED. No arbitrary test code writes the journal or ledger.
- **Validation/epoch fail-closed:** malformed quantity returns 400 and writes neither a journal record nor a Sale Draft. Stale deployment epoch returns 409 and also writes neither. These **do not** constitute a fault-injected mid-transaction rollback proof; that separate oracle stays NOT VERIFIED.
- **Authorization:** an independent live SQL-backed authenticated principal without Sales Read receives 403 on Sales.List while their own `auth/me` remains 200.

Job output uploads **only** `vps04a-live-https-sql.trx`, validates exactly 4 executed/passed, 0 failed/skipped, and binds evidence to the GitHub Actions run SHA. A refused startup, missing secret, missing SQL data, failing TLS validation, stale schema, inaccessible SQL, or any fixture failure is a **failed/blocked gate**, not a skip or approved exception.

## Acceptance ledger template

After the **same final PR HEAD** runs, record the following in the PR/evidence ledger: `HEAD_SHA`, `RUN_URL`, `RUN_SHA`, Linux/SDK image, SQL engine version and exact13 fingerprint (no host/address/credentials), TLS verified result, 178-file count, vector count, 4/4 live result, audit/rollback gaps and final owner decision. The workflow is intentionally **manual** to prevent destructive SQL acceptance from every PR push. Green ordinary SQL-free CI **never** upgrades live gates to PASS.

Do not merge PR #9 as a fully accepted VPS-04A runtime until the independent live gate on final HEAD has executed successfully and the required evidence has been reviewed.


## GitHub Environment provisioning — operator-only execution

**Owner authorization:** approved to prepare the gate, **not** evidence that the target database is isolated or disposable. GitHub's workflow reference alone can auto-create a missing Environment **without secrets or protection rules**. Never add the trigger label before an independent admin has verified Required reviewers, prevent self-review, exact `refs/pull/9/merge` deployment ref restriction (not a source-branch policy), and Environment-scoped secret/variable names.

Chat-mode GitHub integration can update repository files/PRs but cannot write or inspect Environment settings or secrets. Use the reviewed [local admission helper](../../scripts/configure-vps04a-live-gate.py) from an authenticated operator machine. It uses GitHub CLI API calls, requires a separate reviewer login, creates the dedicated Environment with Required reviewers and the exact PR #9 merge-ref deployment policy, and **never accepts/logs secret values**. It does not silently label the PR:

```powershell
# On an operator workstation, with GitHub CLI and Python 3:
gh auth login
gh auth status
python scripts/configure-vps04a-live-gate.py configure --reviewer INDEPENDENT_REVIEWER_GITHUB_LOGIN
```

Then open the repository **Settings → Environments → vps04a-sql-nonproduction** and ensure admin-bypass is disabled; the helper fails closed if the API cannot confirm it. Store the following values **only** as GitHub **Environment secrets**, using Settings or `gh secret set -e vps04a-sql-nonproduction -R chung6a8m/MyDmsVn-Vps SECRET_NAME`: `VPS04A_RUNTIME_SQL`, `VPS04A_ORACLE_SQL`, `VPS04A_SUPERUSER_USERNAME`, `VPS04A_JWT_AUDIENCE`, `VPS04A_JWT_KID`, `VPS04A_JWT_KEYS`, `VPS04A_WRITER_USERNAME`, `VPS04A_WRITER_PASSWORD`, `VPS04A_DENIED_USERNAME`, `VPS04A_DENIED_PASSWORD`. Ensure SQL connections use server-certificate verification, encryption and distinct restricted runtime/read-only oracle identities; never use production or administrator SQL credentials.

Store exactly four environment variables with real nonproduction seed IDs: `VPS04A_CUSTOMER_ID`, `VPS04A_WAREHOUSE_ID`, `VPS04A_EMPLOYEE_ID`, `VPS04A_PRODUCT_ID`. The helper only checks **names**, not the confidentiality, validity or privileges of their values. Operator must independently inspect schema13 migration journal/fingerprint, backup/disposal ownership, dedicated database, active seed rows/permissions, GitHub runner-to-SQL connectivity and SQL TLS certificate chain.

```powershell
# Read-only preflight, then explicitly reviewed final-SHA trigger:
python scripts/configure-vps04a-live-gate.py verify
python scripts/configure-vps04a-live-gate.py trigger --expected-sha FULL_40_CHARACTER_REVIEWED_PR_HEAD_SHA --confirm approved-isolated-sql13
```

If preflight fails, **do not** add `vps04a-live-approved`; configure the missing protected resources first. The label only **requests** the deployment; a separate reviewer must approve the Environment gate in GitHub Actions. A later PR code change invalidates the earlier SHA: remove and reapply the label **only** after the new commit's CI passes and the protected preflight has been repeated. The workflow must produce 4/4 verified live cases and a real SQL oracle; neither a protected Environment nor a successful static CI is a live PASS.

**PR ref rules / safe migration:** GitHub compares the Environment deployment allowlist to `GITHUB_REF`; PR #9's labeled workflow uses `refs/pull/9/merge`. The original helper configured the source branch name, which would block the protected job. `configure` adds `refs/pull/9/merge` **before** deleting only its obsolete original exact source-branch policy, avoiding an empty-rule interval if the replacement fails and rejects any unknown/paginated policies. `verify` rejects any extra policy. Do not use `refs/pull/*/merge`, which broadens access to unrelated PRs. In-workflow PR identity guards, exact-HEAD checkout and independent before/after PR HEAD checks protect the evidence. This correction does not certify actual SQL execution.
