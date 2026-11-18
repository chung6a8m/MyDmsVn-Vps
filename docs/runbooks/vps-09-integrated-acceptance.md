# VPS-09 — integrated Windows Desktop / Linux Host / SQL release acceptance

Scope: [Issue #12](https://github.com/chung6a8m/MyDmsVn-Vps/issues/12).
The live status authority is [docs/roadmap/README.md](../roadmap/README.md).
This runbook is **not** proof of a physical environment or authorization for production.

## Decision and prerequisites

Owner decisions OWN-VPS-01..03 remain binding: Linux VPS hosts ASP.NET Core net10; only Windows Desktop net48; SQL Server existing and fresh-install paths must both be supported. Do not inherit an old repository's PASS results. VPS-08 [PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27) has **source-only** approval: Issue #11 remains OPEN for systemd/Nginx/TLS/SQL staged execution. It is a hard predecessor of VPS-09, not an accepted operational stage.

Before dispatch, the independent operator must obtain:

1. An **exact 40-hex reviewed main commit**, all required GitHub workflow conclusions and external review on that same commit; record the names, run IDs and counts, not just green summary badges. Include the new VPS-09 source workflow.
2. Protected independent-review staging (VPS08-01..08) with isolated SQL13, verified SQL TLS/hostname, runtime credentials of least privilege, a *different read-only SQL Oracle principal*, verified DbUp/FluentMigrator journal/fingerprint, real backups and restores. No production SQL may be targeted.
3. A real Linux VPS, pinned immutable deployed Host artifact matching the reviewed commit, Nginx with public HTTPS and strict loopback admission, systemd/audit/credentials, service restart, SIGKILL, **real reboot**, broken deploy, compatible-binary rollback and SQL/audit reconciliation. Preserve actual sanitized evidence from [VPS-08 runbook](vps-08-linux-deployment.md).
4. An interactive (non-Session-0) Windows x86/.NET Framework 4.8 machine with SDK 8.0.400 and 10.0.404, the exact source checkout and a real hosted Desktop application. No CI headless test is an attended GUI test. Independent reviewer records screen/native checkpoints at 100% and 125% where applicable, plus session/permissions/Unknown recovery.
5. Immutable outcome/side-effect oracles: all 57 routes with 293 applicable live value/variant observations (including historical 28 source-only VPS-02 routes), positive/negative WEB5 terminal/operator and enabled WEB6 protected audit, session/JWT, denied permissions, transactional fail/retry, oversell, idempotency, multiwarehouse, decimal/COGS, migration and cleanup. Use actual expected/actual/diff and independent SQL source/ledger/stock/COGS queries. The 293 SQL-free 05C observations and 58 disposable 07B migration cases do **not** satisfy these real SQL gates.

## Automated parts delivered by the VPS-09 implementation PR

- `python3 scripts/vps09_verify.py inventory` checks the independently frozen 57-route / 293-pair inventory plus the 14-gate registry. It does **not** send any HTTP or SQL traffic.
- `python3 -m unittest discover -s scripts/tests -p 'test_vps09_verify.py' -v` tests Ed25519 receipt admission and refusal (tampered signature, SHA, skipped gate, gaps, fake staging, SQL Oracle diff, symlink, missing CI, untrusted key and more). Synthetic fixtures can never serve as release evidence.
- [.github/workflows/vps-09-evidence.yml](../../.github/workflows/vps-09-evidence.yml) runs the preceding source checks on PR/main. Its manually dispatched protected Windows job performs **only a bounded x86 publish + real remote HTTPS/SQL suite (4 selected existing facts)**, never GUI or release closeout. The separate protected reviewer job validates a previously completed signed packet. Neither runs on normal PR or push.
- `scripts/Invoke-Vps09WindowsAcceptance.ps1` refuses missing opt-in, non-HTTPS/noncanonical origin, untrusted SQL Oracle TLS, moved SHA, unpinned SDK, wrong x86 PE or skipped TRX. Its sanitized summary explicitly says that attended GUI, physical remote binary identity, 57-route real SQL and release are not verified. Never elevate 4/4 bounded tests into whole-system PASS.

The full 293-route live-SQL runner, unattended Linux reboot/rollback orchestration and attended GUI are **not implemented by this source increment**; their real external evidence is mandatory before a signed full packet can pass. Do not create placeholder PASS rows.

## Protected workflow environment admission

The nonproduction protected Environment `vps09-fullstack-nonproduction` requires independent reviewers, prevention of self-review, admin bypass disabled, exact main ref and a Windows self-hosted runner labeled `vps09-attended`. This runner must have actual GUI capabilities and verified separate SQL read-only Oracle access. Provision, through GitHub Environment (not source):

- Secret `VPS09_ISOLATED_SQL_ADMISSION` = `reviewed-isolated-sql13-only`; secret `VPS09_READONLY_SQL_ORACLE` with `Encrypt=True;TrustServerCertificate=False` and server certificate CA/hostname verification; secret `VPS09_JWT_AUDIENCE`.
- Dedicated seeded nonproduction writer/denied credentials `VPS09_WRITER_USERNAME`, `VPS09_WRITER_PASSWORD`, `VPS09_DENIED_USERNAME`, `VPS09_DENIED_PASSWORD`; never reuse an admin, production or Oracle identity.
- Variable `VPS09_PUBLIC_ORIGIN` (canonical public HTTPS, no path/query); seed IDs `VPS09_CUSTOMER_ID`, `VPS09_WAREHOUSE_ID`, `VPS09_EMPLOYEE_ID`, `VPS09_PRODUCT_ID`. Operators must independently prove these are isolated disposable test fixtures.

The second protected Environment `vps09-release-review` requires a separate Linux self-hosted runner labeled `vps09-reviewer`, independent human approval and admin-bypass protection. Its read-only variables: `VPS09_REVIEW_PACKET_PATH` (pre-staged complete packet directory), `VPS09_OWNER_PUBKEY_PATH` (trusted reviewed ED25519 PEM), `VPS09_OWNER_PUBKEY_SHA256` (lowercase SHA-256 of **literal PEM bytes**, fingerprint pinned outside the packet). No private signing key or SQL credential may be stored in the repository. The review runner checks *structural integrity and approved assertions*, not the truth of a real machine event.

Manual workflow can be invoked on main only after actual protected Environment installation and an independently reviewed exact SHA:

```bash
gh workflow run vps-09-evidence.yml --repo chung6a8m/MyDmsVn-Vps --ref main \
  -f reviewed_sha=EXACT_40_HEX_MAIN_SHA \
  -f approved_scope=reviewed-isolated-fullstack-only
```

If protected environments, runners, selected SQL seeds or source SHA are absent, the protected jobs should remain SKIPPED/NOT RUN, not PASS. Do not create or configure these environments using an unreviewed PR.

## Evidence packet and cryptographic contract

Maintain the packet outside source control (protected transfer, access/retention policy). It contains `attestation.json`, raw 64-byte `attestation.sig` and only the relative evidence files listed under `attestation.artifacts` with exact SHA-256 and byte count. Symlinks, unexpected files, parent traversals, file digest/size mismatches and missing gate evidence are refused.

The signed JSON fields are defined by the code and [gate registry](../testing/vps-09-gates.json). Key requirements:

- `repository` = `chung6a8m/MyDmsVn-Vps`, `schemaVersion` = 1; `headSha`, `desktopSourceSha` and `linuxDeployedSourceSha` must all equal the reviewer-pinned exact main SHA. Distinct SHA-256 digests bind Desktop executable, Linux package, machine identity and approved isolated SQL instance.
- `environment` contains the required exact Windows net48-x86 attended / Linux net10 systemd+Nginx / approved isolated SQL13 / verified TLS / independent read-only SQL / protected reviewer / cleanup values. These are **signed assertions**, independently verified against original machine logs, not machine-detected facts.
- Every mandatory `VPS09-01..14` gate must be `PASS`, with positive discovered/executed/passed counts, zero failed/skipped, nonempty runner identity and a distinct primary evidence artifact per gate. No missing or unknown gates.
- `routeObservations` references a hash-bound file with exactly the 293 frozen `(CaseId, Variant)` pairs, no duplicates, each marked protected-real-sql13 with HTTP status matching the applicable frozen per-variant status and an independently derived expected payload digest, actual status/payload digest and empty diff. It does not reuse the SQL-free corpus bytes as proof.
- `sqlOracle` references hash-bound independent read-only SQL expected/actual/diff for all twelve registry scenarios; query fingerprint required. Actual values and queries must be captured during the real run.
- `ciRuns` lists every registry workflow by exact file name with `sourceSha`, `conclusion=success` and GitHub Actions run URL. The verifier cannot query GitHub and the independent reviewer **must verify the run contents, test counts and conclusions against GitHub** before signing.
- `people.operator` differs from `people.reviewer` and `people.owner`. `releaseDecision.status` must be explicitly `APPROVED_FOR_PRODUCTION` with a date and named Owner **only if the Owner actually grants it**. The verifier never signs or creates an Owner decision.

Example command to sign once (on an authorized, offline signer machine; never commit private key):

```bash
openssl pkeyutl -sign -inkey /secure/owner-ed25519-private.pem \
  -rawin -in /secure/vps09-packet/attestation.json \
  -out /secure/vps09-packet/attestation.sig
sha256sum /secure/owner-ed25519-public.pem
python3 scripts/vps09_verify.py verify \
  --bundle /secure/vps09-packet --expected-sha EXACT_40_HEX_MAIN_SHA \
  --trusted-key /secure/owner-ed25519-public.pem \
  --trusted-key-sha256 INDEPENDENTLY_PINNED_64_HEX_SHA256
```

The key must be Ed25519. The signature binds the *statement and hashes*, not the physical truth of a VPS reboot or an independent SQL query. The reviewer must inspect original time-bound Windows/SQL/Linux data, trace IDs, distinct actors, 57/293 live-run counts, expected/actual/diff, cleanup and backup evidence. A self-issued signer key whose fingerprint was not independently approved is not evidence of Owner approval.

## Closeout and release decision

1. Review all predecessor increments on the candidate final main SHA; resolve any P0/P1 finding and rerun **all required** workflows after any change. Do not accept pre-fix tests.
2. Execute the dedicated protected VPS-08 stage, reviewed Windows publish and remote SQL checks, attended GUI, complete 293-case dynamic real-SQL differential and expanded SQL Oracles. Record full counts and actual systemd/reboot/fault events; rerun failed or inconclusive cases, never relabel them as PASS.
3. Run SQL migration fresh/9..13 support, backup/restore, schema/journal and restricted-user no-migration regressions. Independently reconcile business source, ledger, valuation, COGS, permission/session, audit and any durable pending operations before and after fault drills.
4. Confirm cleanup (owned accounts/databases/processes/packages) while preserving evidence and durable protected audit according to retention rules; capture evidence without secrets or customer data.
5. Request independent code/runtime/SQL evidence review; only then request explicit **dated Owner release decision**. An exception must include gate, evidence, reason, scope, compensating controls, Owner and date. A skipped P0 gate cannot be automatically waived.
6. Pin and verify signed bundle; record signer public-key hash, SHA, CI URLs, external environment identity, Owner decision and any exceptions in [VPS-09 evidence](../evidence/vps-09-integrated-closeout.md). Update the canonical roadmap once, after proof. Until then status remains IN PROGRESS / RELEASE BLOCKED.

**Safety rule:** the packet verifier verifies signed declarations and structural coverage, not operational provenance. Reject a fabricated all-green sample. Source-only merge of this tooling does not close Issue #12 or grant deployment approval.
