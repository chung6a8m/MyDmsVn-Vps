# VPS-09 — integrated Windows Desktop / Linux Host / SQL release acceptance

Scope: [Issue #12](https://github.com/chung6a8m/MyDmsVn-Vps/issues/12).
The live status authority is [docs/roadmap/README.md](../roadmap/README.md).
This runbook is **not** proof of a physical environment or authorization for production.

## Decision and prerequisites

Owner decisions OWN-VPS-01..03 remain binding: Linux VPS hosts ASP.NET Core net10; only Windows Desktop net48; SQL Server existing and fresh-install paths must both be supported. OWN-VPS-04 permits Owner `chung6a8m` to perform the human operator/reviewer/release roles for only the Issues #11/#12 candidate, with `NO_INDEPENDENT_HUMAN_REVIEW`, two distinct runners and a delayed second pass. Do not inherit an old repository's PASS results. VPS-08 [PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27) has **source-only** approval; VPS08-01..08 remain a hard predecessor of VPS-09.

Before dispatch, the Owner acting under OWN-VPS-04 must obtain:

1. An **exact 40-hex reviewed main commit** and all 11 required GitHub workflow conclusions on that same commit; record names, run IDs and counts, not just green summary badges.
2. Protected OWN-VPS-04 staging (VPS08-01..08) with isolated SQL13, verified SQL TLS/hostname, runtime credentials of least privilege, a *different read-only SQL Oracle principal*, verified DbUp/FluentMigrator journal/fingerprint, real backups and restores. No production SQL may be targeted.
3. A real Linux VPS, pinned immutable deployed Host artifact matching the reviewed commit, Nginx with public HTTPS and strict loopback admission, systemd/audit/credentials, service restart, SIGKILL, **real reboot**, broken deploy, compatible-binary rollback and SQL/audit reconciliation. Preserve actual sanitized evidence from [VPS-08 runbook](vps-08-linux-deployment.md).
4. An interactive (non-Session-0) Windows x86/.NET Framework 4.8 machine with SDK 8.0.400 and 10.0.404, the exact source checkout and a real hosted Desktop application. No CI headless test is an attended GUI test. The Owner records screen/native checkpoints at 100% and 125%, plus session/permissions/Unknown recovery; a separate Linux machine performs the delayed second pass.
5. Immutable outcome/side-effect oracles: all 57 routes with 293 applicable live value/variant observations (including historical 28 source-only VPS-02 routes), positive/negative WEB5 terminal/operator and enabled WEB6 protected audit, session/JWT, denied permissions, transactional fail/retry, oversell, idempotency, multiwarehouse, decimal/COGS, migration and cleanup. Use actual expected/actual/diff and independent SQL source/ledger/stock/COGS queries. The 293 SQL-free 05C observations and 58 disposable 07B migration cases do **not** satisfy these real SQL gates.

## Automated parts delivered by the VPS-09 implementation PR

- `docs/testing/vps-09-live-scenarios.json` maps the frozen 57 routes and all unique 293 `(CaseId, Variant)` pairs to method/path template, actor, body/header recipes, preconditions, fault phase, expected status and independent-digest rule. Its twelve SQL Oracle definitions identify scenario/fault linkage. Environment-specific IDs, concrete request bodies and expected values remain in protected external plans, never in Git.
- `tools/MyDmsVn.Vps09.Acceptance` is a .NET 10 external orchestrator with `preflight`, `routes`, `oracles` and `summarize`. It rejects a stale/dirty SHA, noncanonical or redirecting origin, unverified SQL TLS, actor gaps, duplicate/missing cases, response/Oracle diffs and incomplete cleanup. `summarize` emits `NOT_APPROVED_BY_TOOL`; it cannot sign a packet or issue a release decision.
- `scripts/vps09_linux_receipt.py` and `scripts/vps09_gui_checklist.py` hash externally captured Linux and attended-GUI evidence. They validate fixed event/checkpoint sets and always leave physical evidence review required. `scripts/vps09_packet_builder.py` produces only an unsigned artifact hash index.
- `scripts/Invoke-Vps09IntegratedAcceptance.ps1` publishes Desktop net48 x86 and the orchestrator at the exact clean SHA. `Prepare` logs in three protected actors, keeps bearer tokens only in process memory, executes 293 routes and 12 Oracles, and creates a 9-checkpoint GUI template. `Finalize` validates the operator-completed GUI and Linux receipts plus explicit owned-resource cleanup. No production API test hook is introduced.
- `python3 scripts/vps09_verify.py inventory` checks the independently frozen 57-route / 293-pair inventory plus the 14-gate registry. It does **not** send any HTTP or SQL traffic. The signed verifier rejects duplicate JSON members and uses an immutable byte snapshot for OpenSSL signature verification, avoiding a path swap between verification and parsing. The verifier also rehashes the precise observation/SQL-Oracle JSON bytes it parses, so a file swapped after the artifact-ledger scan cannot pass.
- `python3 -m unittest discover -s scripts/tests -p 'test_vps09_verify.py' -v` tests Ed25519 receipt admission and refusal (tampered signature, SHA, skipped gate, gaps, fake staging, SQL Oracle diff, symlink, missing CI, untrusted key and more). Synthetic fixtures can never serve as release evidence.
- [.github/workflows/vps-09-evidence.yml](../../.github/workflows/vps-09-evidence.yml) runs Linux/Python/.NET source checks **and a Windows-2022 PowerShell parser / missing-approval refusal gate** on PR/main. Manual phases are `bounded`, `prepare`, `finalize` and `review`; protected jobs never run on ordinary PR/push. The review phase only verifies a previously signed packet.
- `scripts/Invoke-Vps09WindowsAcceptance.ps1` refuses missing opt-in, non-HTTPS/noncanonical origin, untrusted SQL Oracle TLS, moved SHA, any tracked or untracked source modification, a pre-existing evidence output directory, unpinned SDK, wrong x86 PE or skipped TRX. Its sanitized summary explicitly says that attended GUI, physical remote binary identity, 57-route real SQL and release are not verified. Never elevate 4/4 bounded tests into whole-system PASS.

The tools automate request/Oracle execution and evidence structure, but they do not manufacture physical reboot/rollback or attended GUI proof. The Owner must perform and later re-inspect those actions after the 24-hour cooling period. This is explicitly lower assurance than independent human review. Do not create placeholder PASS rows.

## Protected workflow environment admission

The nonproduction Environment `vps09-fullstack-nonproduction` has zero required reviewers under OWN-VPS-04, admin bypass disabled, exact main ref and exactly one Windows self-hosted runner labeled `vps09-attended`. This runner must have actual GUI capabilities and verified separate SQL read-only Oracle access. Provision, through GitHub Environment (not source):

- Secret `VPS09_ISOLATED_SQL_ADMISSION` = `reviewed-isolated-sql13-only`; secret `VPS09_READONLY_SQL_ORACLE` with `Encrypt=True;TrustServerCertificate=False` and server certificate CA/hostname verification; secret `VPS09_JWT_AUDIENCE`.
- Dedicated seeded nonproduction writer/denied/operator credentials `VPS09_WRITER_USERNAME`, `VPS09_WRITER_PASSWORD`, `VPS09_DENIED_USERNAME`, `VPS09_DENIED_PASSWORD`, `VPS09_OPERATOR_USERNAME`, `VPS09_OPERATOR_PASSWORD`; never reuse an admin, production or Oracle identity. `VPS09_CLEANUP_ATTESTATION=confirmed-owned-nonproduction-cleanup` is provided only after cleanup has actually completed.
- Variable `VPS09_PUBLIC_ORIGIN` (canonical public HTTPS, no path/query); seed IDs `VPS09_CUSTOMER_ID`, `VPS09_WAREHOUSE_ID`, `VPS09_EMPLOYEE_ID`, `VPS09_PRODUCT_ID`; protected paths `VPS09_ROUTE_PLAN_PATH`, `VPS09_ORACLE_PLAN_PATH`, reviewed lowercase SHA-256 pins `VPS09_ROUTE_PLAN_SHA256`, `VPS09_ORACLE_PLAN_SHA256`, `VPS09_EVIDENCE_ROOT`, `VPS09_GUI_CHECKLIST_PATH`, `VPS09_LINUX_RECEIPT_PATH`; `VPS09_OWNER=chung6a8m`; frozen `VPS09_CLOSEOUT_CANDIDATE_SHA`; and `VPS09_FIRST_PASS_RUNNER_SHA256`, the lowercase SHA-256 of the Windows MachineGuid UTF-8 bytes. The three protected application actors remain distinct from the human governance role.

The second Environment `vps09-release-review` has zero required reviewers and uses a separate Linux self-hosted runner labeled `vps09-reviewer`, exact main and disabled admin bypass. Its read-only variables are `VPS09_REVIEW_PACKET_PATH`, `VPS09_OWNER_PUBKEY_PATH`, `VPS09_OWNER_PUBKEY_SHA256` (lowercase SHA-256 of literal PEM bytes, pinned outside the packet before acceptance), both `VPS09_FIRST_PASS_RUNNER_SHA256` and `VPS09_SECOND_PASS_RUNNER_SHA256`, frozen `VPS09_CLOSEOUT_CANDIDATE_SHA` and `VPS09_OWNER=chung6a8m`. No private signing key or SQL credential may be stored in the repository. The runner verifies structural integrity and approved assertions, not the truth of a physical event.

The Owner configures zero-reviewer exact-main admission, then disables admin bypass and provisions the settings. Verification requires exact main, all setting names and exactly one online dedicated runner, but cannot inspect secret values or physical isolation:

```bash
python3 scripts/configure_vps09_gates.py configure \
  --environment vps09-fullstack-nonproduction
python3 scripts/configure_vps09_gates.py verify \
  --environment vps09-fullstack-nonproduction --expected-sha EXACT_40_HEX_MAIN_SHA \
  --admin-bypass-attestation confirmed-disabled:OWN-VPS-04:vps09-fullstack-nonproduction:EXACT_40_HEX_MAIN_SHA
```

The attestation is logged because the documented GitHub Environment API does not expose admin-bypass state; the Owner must inspect Settings directly. Missing protection-rule data blocks verification. Once the candidate is approved or rejected, remove `VPS09_CLOSEOUT_CANDIDATE_SHA` or disable every affected Environment; changing the pin does not authorize another candidate.

The protected route plan contains exactly 293 concrete cases. Each case must match the tracked manifest and contains `caseId`, `variant`, `method`, concrete `/api/...` path, JSON body or null, actor, non-secret headers, expected status and a 64-hex expected body digest derived independently from real-SQL source/oracle rules. It maps actors to protected process environment names, for example `writer -> VPS09_WRITER_BEARER_TOKEN`; never place a bearer token in the file. The Oracle plan contains exactly twelve unique scenarios, read-only single-statement `SELECT`/`WITH` queries and independently prepared canonical expected rows. Mutating or multi-statement Oracle SQL is refused.

Minimal shapes (illustrative placeholders only; not acceptance data):

```json
{
  "manifestPath": "C:/reviewed/docs/testing/vps-09-live-scenarios.json",
  "origin": "https://staging.example",
  "runner": "windows-attended-machine-hash",
  "evidenceArtifact": "route-observations.json",
  "expectedProvenance": "independent-real-sql13-oracle",
  "actorTokenEnvironment": {
    "writer": "VPS09_WRITER_BEARER_TOKEN",
    "denied": "VPS09_DENIED_BEARER_TOKEN",
    "operator": "VPS09_OPERATOR_BEARER_TOKEN"
  },
  "cases": [{
    "caseId": "VPS02-EXAMPLE",
    "variant": "success",
    "method": "POST",
    "path": "/api/v1/example/123",
    "body": "{\"ownedFixtureId\":123}",
    "actor": "writer",
    "headers": { "X-Correlation-Id": "candidate-owned-id" },
    "expectedStatus": 200,
    "expectedBodySha256": "64-lowercase-hex-from-independent-oracle"
  }]
}
```

```json
{
  "sqlOracleConnectionStringEnvironment": "VPS04A_SQL_ORACLE_CONNECTION_STRING",
  "runner": "windows-attended-machine-hash",
  "evidenceArtifactPrefix": "oracle-",
  "requiredScenarios": ["all-twelve-registry-names"],
  "plans": [{
    "scenario": "one-registry-name",
    "query": "SELECT ... FROM ... WHERE OwnedFixtureId = 123 ORDER BY ...",
    "expectedCanonicalJson": "[{\"Column\":\"independently-derived-value\"}]"
  }]
}
```

The real files must list 293 and 12 entries respectively; these one-row snippets intentionally cannot pass admission. The Owner must hash and pin the complete protected route and Oracle plans in GitHub Environment variables before dispatch. The CLI pins the public origin and actor token sources, requires the tracked manifest and exact twelve Oracle registry names, and checks route coverage against that manifest again at summarize time. Never derive expected digests from the responses under test.

Manual workflow can be invoked on main only after actual Environment installation and an Owner-reviewed exact SHA. Run `prepare`, complete the attended checklist and Linux receipt, confirm cleanup, then run `finalize`; use a new evidence directory for every candidate:

```bash
gh workflow run vps-09-evidence.yml --repo chung6a8m/MyDmsVn-Vps --ref main \
  -f reviewed_sha=EXACT_40_HEX_MAIN_SHA \
  -f approved_scope=reviewed-isolated-fullstack-only -f acceptance_phase=prepare
gh workflow run vps-09-evidence.yml --repo chung6a8m/MyDmsVn-Vps --ref main \
  -f reviewed_sha=EXACT_40_HEX_MAIN_SHA \
  -f approved_scope=reviewed-isolated-fullstack-only -f acceptance_phase=finalize
```

If protected environments, runners, selected SQL seeds or source SHA are absent, the protected jobs should remain SKIPPED/NOT RUN, not PASS. Do not create or configure these environments using an unreviewed PR.

Finalize requires the completed schema-v2 GUI checklist to match the reviewed source SHA, OWN-VPS-04, Owner identity, first-pass runner and actual published Desktop executable SHA-256, not merely any syntactically valid nine-row checklist. The Owner fills nine distinct evidence digests: startup, login/session, permissions, sales workflow and inventory workflow at 100%; startup, login/session, Unknown recovery and logout at 125%. Validate only from an interactive non-Session-0 process. Screenshots/logs stay in protected storage and must be checked for secrets/customer data before retention; the Owner rehashes them during the delayed second pass.

## Evidence packet and cryptographic contract

Maintain the packet outside source control (protected transfer, access/retention policy). It contains `attestation.json`, raw 64-byte `attestation.sig` and only the relative evidence files listed under `attestation.artifacts` with exact SHA-256 and byte count. Symlinks, unexpected files, parent traversals, file digest/size mismatches and missing gate evidence are refused.

The signed JSON fields are defined by the code and [gate registry](../testing/vps-09-gates.json). The signed gate counts must meet registry floors, notably 293 real-SQL route variants, 8 VPS-08 operational checks, 9 attended GUI checkpoints, 12 valuation/oracle scenarios and 11 same-SHA workflows. Key requirements:

- `repository` = `chung6a8m/MyDmsVn-Vps`, `schemaVersion` = 2; `headSha`, `desktopSourceSha` and `linuxDeployedSourceSha` all equal the Owner-pinned exact main SHA. Distinct SHA-256 digests bind Desktop executable, Linux package, machine identity and approved isolated SQL instance.
- `governance` contains mode `single-owner-exception`, decision `OWN-VPS-04`, assurance `NO_INDEPENDENT_HUMAN_REVIEW`, Owner `chung6a8m`, the pre-pinned Owner public-key hash, distinct first/second-pass runner hashes and strict `finalizedAtUtc`.
- `preflightReceipt`, `linuxReceipt` and `guiReceipt` reference hash-bound schema-v2 typed receipts with the exact assurance string. Linux covers all eight named events and binds the deployed machine/artifact; GUI carries all nine checkpoints and binds the Desktop executable and externally pinned first runner.
- `finalizationIndex` hashes the complete finalized core evidence, and `finalizationReceipt` binds that index to the protected finalize workflow URL and its UTC timestamp. `governance.finalizedAtUtc` must equal that receipt, occur no earlier than OWN-VPS-04, and precede approval by at least 24 hours.
- `environment` contains exact Windows net48-x86 attended / Linux net10 systemd+Nginx / approved isolated SQL13 / verified TLS / separate read-only SQL / protected single-Owner staging / cleanup values. These are signed Owner assertions checked against original machine logs during the delayed second pass, not automatically detected physical facts.
- Every mandatory `VPS09-01..14` gate must be `PASS`, with positive discovered/executed/passed counts, zero failed/skipped, nonempty runner identity and a distinct primary evidence artifact per gate. No missing or unknown gates.
- `routeObservations` references a hash-bound file with exactly the 293 frozen `(CaseId, Variant)` pairs, no duplicates, each marked protected-real-sql13 with HTTP status matching the applicable frozen per-variant status and an independently derived expected payload digest, actual status/payload digest and empty diff. It does not reuse the SQL-free corpus bytes as proof.
- `sqlOracle` references hash-bound independent read-only SQL expected/actual/diff for all twelve registry scenarios; query fingerprint required. Actual values and queries must be captured during the real run.
- `ciRuns` lists every registry workflow by exact file name with `sourceSha`, `conclusion=success` and GitHub Actions run URL. The verifier cannot query GitHub; the Owner must verify run contents, counts and conclusions during the second pass.
- `releaseDecision.status` is `APPROVED_FOR_PRODUCTION`, `owner` is `chung6a8m`, `sourceSha` is the candidate and `approvedAtUtc` is at least 24 hours after `governance.finalizedAtUtc`. The verifier never signs or creates the decision.

Example command to sign once (on an authorized, offline signer machine; never commit private key):

```bash
openssl pkeyutl -sign -inkey /secure/owner-ed25519-private.pem \
  -rawin -in /secure/vps09-packet/attestation.json \
  -out /secure/vps09-packet/attestation.sig
sha256sum /secure/owner-ed25519-public.pem
python3 scripts/vps09_verify.py verify \
  --bundle /secure/vps09-packet --expected-sha EXACT_40_HEX_MAIN_SHA \
  --trusted-key /secure/owner-ed25519-public.pem \
  --trusted-key-sha256 PREPINNED_64_HEX_SHA256 \
  --expected-first-pass-runner-sha256 FIRST_PASS_MACHINE_GUID_SHA256 \
  --expected-second-pass-runner-sha256 SECOND_PASS_MACHINE_ID_SHA256
```

The key must be Ed25519. Its public fingerprint must be pinned outside the packet before acceptance; the private key remains offline. The signature binds statements and hashes, not the physical truth of a reboot or SQL query. After at least 24 hours, the Owner must inspect original time-bound Windows/SQL/Linux data, trace IDs, distinct application actors, 57/293 counts, expected/actual/diff, cleanup and backup evidence. The resulting assurance remains explicitly lower than independent human review.

## Closeout and release decision

1. Review all predecessor increments on the candidate final main SHA; resolve any P0/P1 finding and rerun **all required** workflows after any change. Do not accept pre-fix tests.
2. Execute the dedicated protected VPS-08 stage, reviewed Windows publish and remote SQL checks, attended GUI, complete 293-case dynamic real-SQL differential and expanded SQL Oracles. Record full counts and actual systemd/reboot/fault events; rerun failed or inconclusive cases, never relabel them as PASS.
3. Run SQL migration fresh/9..13 support, backup/restore, schema/journal and restricted-user no-migration regressions. Independently reconcile business source, ledger, valuation, COGS, permission/session, audit and any durable pending operations before and after fault drills.
4. Confirm cleanup (owned accounts/databases/processes/packages) while preserving evidence and durable protected audit according to retention rules; capture evidence without secrets or customer data.
5. Finalize the unsigned packet, record `finalizedAtUtc`, then wait at least 24 hours. On the distinct second-pass runner, the Owner reviews original code/runtime/SQL evidence and only then creates the explicit Owner release decision. A skipped P0 gate cannot be waived by OWN-VPS-04.
6. Sign and verify the bundle; record public-key hash, both runner hashes, SHA, CI URLs, environment identity, Owner decision and `NO_INDEPENDENT_HUMAN_REVIEW` in [VPS-09 evidence](../evidence/vps-09-integrated-closeout.md). Update the canonical roadmap only after proof. Until then status remains IN PROGRESS / RELEASE BLOCKED.

**Safety rule:** the packet verifier verifies signed declarations and structural coverage, not operational provenance. Reject a fabricated all-green sample. Source-only merge of this tooling does not close Issue #12 or grant deployment approval.
