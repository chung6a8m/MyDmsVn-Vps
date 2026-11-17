# VPS-03 Issue #8 Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and execute a fail-closed acceptance harness that proves PR #6's exact Windows x86 Desktop communicates through the real HTTPS HttpHost to isolated SQL for all Issue #8 requirements.

**Architecture:** A new opt-in `net48` x86 xUnit project owns the live environment, 46-method production HTTP-adapter matrix, SQL oracles, session/permission cases, and attended UI probes. A thin PowerShell entry point builds exact-HEAD Desktop/host packages, runs the suite, validates non-empty TRX and manifest coverage, and emits sanitized evidence; normal CI only compiles/discovers this suite and never claims live acceptance.

**Tech Stack:** C# 12 / .NET Framework 4.8, xUnit v3 + VSTest, `MyDmsVn.Desktop.Infrastructure.Http`, IIS Express HTTPS, Microsoft.Data.SqlClient, existing DbUp migrations and SQL test ownership helpers, Windows UI Automation, PowerShell 7.

**Spec:** `docs/superpowers/specs/2026-11-17-vps-03-issue-8-acceptance-design.md`

## Global Constraints

- Preserve current authentication, permission, transaction, inventory/costing, idempotency, JSON wire, and schema behavior.
- Do not retarget production projects, replace DbUp/MediatR/Newtonsoft, remove the historical Local adapter, or rename namespaces.
- The acceptance Desktop must be Windows `net48` x86 bytes built from the exact PR HEAD; its process receives no SQL connection variable or provider.
- The host must be actual HTTPS HttpHost at one canonical configured origin with `MYDMSVN_WEB_DESKTOP_API=enabled`; certificate validation stays enabled.
- Schema 11, 12, and 13 are distinct required paths, and the host uses only the provisioned restricted runtime principal.
- Required skips, not-executed cases, empty filters, missing TRX, cleanup uncertainty, or source/build/static-only results are failures, never PASS.
- Never log or commit SQL connection strings, passwords, JWTs, refresh cookies, signing keys, or certificate private material.
- PR #6 remains draft and VPS-03 remains `IN PROGRESS` unless every Issue #8 gate passes at the same final HEAD and final-HEAD CI is rerun.

## Review Focus

- A package built from a different SHA or architecture must be rejected before SQL mutation (`Vps03AcceptancePreflightTests.Rejects_package_sha_or_architecture_mismatch`, Task 2).
- A trusted certificate for the wrong authority/thumbprint must not satisfy HTTPS admission (`Vps03OwnedEnvironmentTests.Rejects_tls_authority_or_thumbprint_mismatch`, Task 3).
- A manifest entry that names a valid route but never invokes the production adapter must fail coverage (`Vps03AcceptanceManifestTests.Rejects_unbound_or_duplicate_method_cases`, Task 1).
- Lost ACK, cancellation, or refresh races must retain UNKNOWN and must not cause a second Post (`Vps03TransactionAcceptanceTests.Lost_ack_and_refresh_race_never_duplicate_post`, Task 6).
- A failed/aborted attended step or uncertain child-process/SQL cleanup must produce a failed evidence record and retain diagnostics (`Vps03AttendedGuiAcceptanceTests.Aborted_walkthrough_and_uncertain_cleanup_cannot_pass`, Task 7).

---

### Task 1: Acceptance project and complete case manifest

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/packages.lock.json`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/xunit.runner.json`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/vps03-issue8-case-manifest.json`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceManifest.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceManifestTests.cs`
- Modify: `MyDmsVn.sln`
- Modify: `scripts/verify-vps-01.py`
- Modify: `.github/workflows/vps-01-tests.yml`
- Modify: `docs/testing/test-policy.md`
- Modify: `tests/README.md`

**Interfaces:**
- Consumes: `docs/contracts/vps-02-desktop-method-matrix.json` and the Issue #8 acceptance matrix.
- Produces: `Vps03AcceptanceManifest.Load(string path)`, `Vps03AcceptanceManifest.Validate(JObject sourceMatrix)`, and `IReadOnlyList<Vps03AcceptanceCase> Cases`; each case has `CaseId`, `Kind`, `Interface`, `Method`, `HttpMethod`, `Path`, and `Executor`.

- [ ] **Step 1: Add the new test project scaffolding and write failing manifest tests**

  Add tests `Contains_exactly_the_46_remote_method_identities`, `Maps_every_issue8_gate_to_a_stable_case`, and `Rejects_unbound_or_duplicate_method_cases`. Assert exact set equality with `vps-02-desktop-method-matrix.json`, unique stable IDs, nonempty executors, and required gate keys `package`, `tls`, `schema11`, `schema12`, `schema13`, `auth`, `permissions`, `sessions`, `transactions`, `lost-ack`, `inventory`, `gui`, and `cleanup`.

- [ ] **Step 2: Run the focused test to verify RED**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter FullyQualifiedName~Vps03AcceptanceManifestTests`

  Expected: FAIL because `Vps03AcceptanceManifest` and/or the manifest entries do not exist.

- [ ] **Step 3: Implement the manifest loader/validator and all 46 method rows**

  Use the exact interface/method/verb/path values in `docs/contracts/vps-02-desktop-method-matrix.json`; add explicit non-method gate rows without disguising them as remote methods. Add the project to `MyDmsVn.sln`, update topology from 12 to 13 executable suites, and add a Windows compile/discovery matrix row with final floor `minimum_tests: 60` and wording that discovery is not acceptance.

- [ ] **Step 4: Generate and lock restore, then verify GREEN and topology**

  Run: `dotnet restore tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj --use-lock-file`; then rerun with `--locked-mode`, rerun the focused test, and run `python scripts/verify-vps-01.py`.

  Expected: manifest tests PASS; static topology reports 13 executable xUnit v3 projects and retains all existing route/fixture checks.

- [ ] **Step 5: Commit the manifest boundary**

  Run: `git add MyDmsVn.sln .github/workflows/vps-01-tests.yml scripts/verify-vps-01.py docs/testing/test-policy.md tests/README.md tests/MyDmsVn.Vps03.Acceptance.Tests && git commit -m "test(vps-03): define Issue 8 acceptance inventory"`

### Task 2: Exact-HEAD preflight and sanitized evidence model

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceOptions.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptancePreflight.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceEvidence.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptancePreflightTests.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceEvidenceTests.cs`

**Interfaces:**
- Consumes: environment values `MYDMSVN_VPS03_ACCEPTANCE=approved`, `MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING`, `MYDMSVN_VPS03_API_ORIGIN`, `MYDMSVN_WEB_TEST_TLS_THUMBPRINT`, `MYDMSVN_VPS03_DESKTOP_PACKAGE`, `MYDMSVN_VPS03_HOST_PACKAGE`, `MYDMSVN_VPS03_HEAD_SHA`, and `MYDMSVN_VPS03_RESULTS_DIRECTORY`.
- Produces: `Vps03AcceptanceOptions.FromEnvironment(Func<string,string?> read)`, `Vps03AcceptancePreflight.Validate(Vps03AcceptanceOptions options)`, `Vps03EvidenceRecord`, `Vps03EvidenceWriter.Write(string directory, Vps03EvidenceRecord record)`, and `Vps03EvidenceWriter.ValidateTrx(string path, IReadOnlyCollection<string> requiredCaseIds)`.

- [ ] **Step 1: Write failing preflight and evidence tests**

  Cover canonical HTTPS root origin, explicit operator acknowledgement, full 40-hex exact HEAD, existing absolute package/result paths, x86 PE header, package provenance SHA, case-insensitive thumbprint normalization, secret-key/name redaction, positive totals, zero required skips/not-executed, required case-ID presence, and `Rejects_package_sha_or_architecture_mismatch`.

- [ ] **Step 2: Run focused tests to verify RED**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter "FullyQualifiedName~Vps03AcceptancePreflightTests|FullyQualifiedName~Vps03AcceptanceEvidenceTests"`

  Expected: FAIL because the options, preflight, and evidence types do not exist.

- [ ] **Step 3: Implement minimal fail-closed parsing, PE/provenance checks, TRX aggregation, and recursive redaction**

  `ValidateTrx` must require `total > 0`, `executed == total`, `passed == executed`, `failed == 0`, and all required case IDs. Evidence JSON/Markdown may include SHA, commands, counts, schema number/fingerprint, public origin authority, package hashes, cleanup state and CI URLs; reject keys matching `connection|string|password|pwd|token|cookie|jwt|secret|key` except the explicit public package-hash fields.

- [ ] **Step 4: Run focused and complete SQL-free acceptance-project tests**

  Run the focused command, then `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --no-restore` without live acceptance variables.

  Expected: unit tests PASS; live rows are discovered but not executed by this SQL-free selection, and no result is labelled Issue #8 PASS.

- [ ] **Step 5: Commit preflight/evidence safety**

  Run: `git add tests/MyDmsVn.Vps03.Acceptance.Tests && git commit -m "test(vps-03): enforce acceptance preflight and evidence safety"`

### Task 3: Owned SQL schemas and actual IIS Express HTTPS host

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceFactAttribute.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03OwnedEnvironment.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03IisExpressHost.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03SqlOracle.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03OwnedEnvironmentTests.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj`

**Interfaces:**
- Consumes: Task 2's validated options; existing `SqlTestDatabase`, `FrozenWeb11Migration`, `DatabaseMigrationRunner.MigrateWeb`, `DatabaseMigrationRunner.MigrateWeb5`, security provisioning, and owned-process patterns.
- Produces: `Vps03OwnedEnvironment.CreateAsync(int schema, CancellationToken token)`, properties `Origin`, `Mode`, `DesktopServices`, `OracleConnectionString`, `DesktopExecutable`, `HeadSha`, and `DisposeAsync()`; `Vps03IisExpressHost.StartAsync(...)`; `Vps03SqlOracle.SchemaFingerprintAsync()` and cleanup/oracle query methods.

- [ ] **Step 1: Write failing environment unit/admission tests**

  Add `Maps_11_12_13_to_legacy_web11_web4_web5`, `Desktop_child_environment_excludes_all_SQL_and_admin_values`, `Rejects_tls_authority_or_thumbprint_mismatch`, `Rejects_unrestricted_runtime_principal`, and `Cleanup_failure_is_terminal_and_preserves_owned_diagnostics` using fakes for process/certificate/SQL metadata.

- [ ] **Step 2: Run focused tests to verify RED**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter FullyQualifiedName~Vps03OwnedEnvironmentTests`

  Expected: FAIL because the owned environment and host admission types do not exist.

- [ ] **Step 3: Implement the owned environment and link existing reviewed test helpers**

  Provision unique databases from the approved master connection; install exact schema 11/12/13; create and SID-check a restricted runtime login/role; generate ephemeral users/JWT keys without logging them; start installed IIS Express hidden with an isolated `applicationhost.config`, exact HTTPS origin, pinned trusted certificate, `MYDMSVN_WEB_DESKTOP_API=enabled`, and only the restricted runtime connection. Compose production HTTP clients with `AddHttpDesktopAdapter`; never use Local/TestServer for live rows. Stop and prove absence of the exact process/request queue before SQL cleanup.

- [ ] **Step 4: Verify GREEN and run one explicitly approved live admission smoke for each schema**

  Run the focused unit command, then run the three `Vps03OwnedEnvironmentLiveTests.Creates_real_https_restricted_environment(schema)` rows through the final runner contract.

  Expected: three real HTTPS admissions PASS with distinct schema fingerprints, zero skips, verified process absence, and verified SQL cleanup. If IIS/certificate/operator inputs are missing, report BLOCKED/NOT RUN and do not continue to Tasks 4–7 live execution.

- [ ] **Step 5: Commit the owned environment**

  Run: `git add tests/MyDmsVn.Vps03.Acceptance.Tests && git commit -m "test(vps-03): own HTTPS and SQL acceptance environment"`

### Task 4: Execute all 46 production Desktop HTTP methods

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03DesktopApiCaseExecutor.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03DesktopApiAcceptanceTests.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceData.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/vps03-issue8-case-manifest.json`

**Interfaces:**
- Consumes: Task 1 case rows and Task 3 `DesktopServices` built from production `AddHttpDesktopAdapter`.
- Produces: `Vps03DesktopApiCaseExecutor.ExecuteAsync(Vps03AcceptanceCase testCase, Vps03OwnedEnvironment environment, CancellationToken token)` and 46 discovered theory rows named by stable case ID.

- [ ] **Step 1: Write the failing 46-row production-adapter theory**

  For every manifest method, seed only through the fixture's administrative provisioning boundary, invoke the named public Desktop interface method through DI, assert the real typed response, and verify route/method plus expected SQL/readback effect. Include Catalog paging/search/active, Sales five methods, Goods five, Inventory two, Identity two, Foundation one, Permission two, and Security five.

- [ ] **Step 2: Run one catalog and one transactional row to verify RED**

  Run the live filter for `VPS03.API.Product.List` and `VPS03.API.Sales.Post`.

  Expected: FAIL because executor bindings/data builders are absent; no row may pass by making a raw `HttpClient` call instead of the production adapter.

- [ ] **Step 3: Implement typed builders and executor bindings incrementally by feature group**

  Implement Catalog, then Identity/Foundation/Permission/Security, then Sales/Goods/Inventory. After each group, run that group's live filter and confirm the manifest's executor binding counter matches its method count.

- [ ] **Step 4: Run all 46 rows on schemas 11/12/13 as applicable and verify GREEN**

  Run the production-adapter matrix through the orchestration entry point; legacy WEB4/WEB5 operation behavior is selected strictly by schema/mode. Validate exactly 46 unique method identities executed per required environment, zero skipped/not-executed, and SQL cleanup PASS.

- [ ] **Step 5: Commit the method matrix**

  Run: `git add tests/MyDmsVn.Vps03.Acceptance.Tests && git commit -m "test(vps-03): exercise all Desktop HTTP methods live"`

### Task 5: Authentication, permissions, sessions, Origin and UI error mapping

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03SessionAcceptanceTests.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03SecurityAcceptanceTests.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03HttpFaultProxy.cs`

**Interfaces:**
- Consumes: Task 3's real HTTPS environment and Task 4's seeded users/roles.
- Produces: stable gate cases for login/me/logout, refresh rotation, account disable, idle expiry, cancellation, Origin mismatch, 401/403/validation/conflict/revision mismatch, and two-session grant changes.

- [ ] **Step 1: Write failing live security/session tests**

  Assert rotating refresh cookie invalidates the prior cookie, logout and account disable revoke current use, idle expiry signs out, cancellation never installs a late session, wrong public Origin is rejected, permission removal is visible to the second session, and every status maps to the existing Desktop error kind/message without leaking server details.

- [ ] **Step 2: Run selected refresh, Origin, and permission rows to verify RED**

  Expected: FAIL because the second-session fixture and bounded fault proxy are absent.

- [ ] **Step 3: Implement the second-session/fault controls without changing production behavior**

  The proxy may drop/delay responses only inside the owned test boundary; it must preserve TLS to the real host and record no credentials/bodies. Use server-side provisioning only for setup/revocation and production Desktop clients for observed behavior.

- [ ] **Step 4: Run the full security/session group and verify GREEN**

  Expected: every required status and lifecycle row executes, refresh-cookie values are never emitted, and SQL/runtime cleanup passes.

- [ ] **Step 5: Commit security/session acceptance**

  Run: `git add tests/MyDmsVn.Vps03.Acceptance.Tests && git commit -m "test(vps-03): verify live Desktop session boundaries"`

### Task 6: Transactions, two-session idempotency, lost ACK, ledger/stock/COGS

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03TransactionAcceptanceTests.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03InventoryOracle.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03LostAckProxy.cs`

**Interfaces:**
- Consumes: Task 3 SQL oracle, Task 4 Sales/Goods builders, Task 5 bounded HTTPS fault control, and production UNKNOWN recovery APIs.
- Produces: SQL snapshots `Vps03InventorySnapshot` and stable cases for rollback, journal/fingerprint, revision conflict, concurrent sessions, lost ACK/readback, and exactly-once Post effects.

- [ ] **Step 1: Write failing transaction/oracle tests**

  Cover failed validation/permission leaves no partial rows; concurrent update/post yields the expected conflict; committed journal fingerprint matches the Desktop intent; one Sales and one Goods Post change ledger/stock/value/COGS exactly once; `Lost_ack_and_refresh_race_never_duplicate_post`; a restart loads the protected UNKNOWN intent; only authenticated matching COMMITTED resolution clears it.

- [ ] **Step 2: Run the lost-ACK and duplicate-Post rows to verify RED**

  Expected: FAIL because the response-drop proxy and independent before/after SQL snapshots are absent.

- [ ] **Step 3: Implement minimal SQL snapshots and deterministic lost-ACK boundary**

  Drop only the response after the real host has committed, never replay the request, then use a second authorized session and independent SQL/journal readback. Assert operation ID, actor, epoch, kind, route scope, target, fingerprint, retained response, ledger cardinality, on-hand quantity/value, and COGS.

- [ ] **Step 4: Run the complete transaction group and verify GREEN**

  Expected: all required schema/mode rows execute sequentially, no duplicate Post or orphaned transaction exists, and both process and SQL cleanup are proven.

- [ ] **Step 5: Commit transaction acceptance**

  Run: `git add tests/MyDmsVn.Vps03.Acceptance.Tests && git commit -m "test(vps-03): prove HTTP SQL transaction invariants"`

### Task 7: Exact published Desktop and attended GUI acceptance

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03DesktopProcess.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03RemoteGuiProbe.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AttendedGuiAcceptanceTests.cs`
- Create: `docs/runbooks/vps-03-issue-8-acceptance.md`

**Interfaces:**
- Consumes: Task 2 exact package preflight, Task 3 owned environment, and Task 6 UNKNOWN scenario.
- Produces: `Vps03DesktopProcess.Start(Vps03OwnedEnvironment environment)`, attended checkpoints with stable case IDs, screenshots/metadata hashes without secrets, and immutable `Vps03AttendedResult` records.

- [ ] **Step 1: Write failing process/UI admission tests**

  Assert child bitness 32, exact executable hash/SHA, SQL/admin environment exclusion, only canonical HTTPS origin/mode, unique process identity, login/shell/workspace lifecycle, 401/403/validation/conflict/UNKNOWN dialog mapping, logout/idle expiry, workspace disposal, and `Aborted_walkthrough_and_uncertain_cleanup_cannot_pass`.

- [ ] **Step 2: Run process admission tests to verify RED**

  Expected: FAIL because the remote Desktop process launcher, UI probe, and signed attended-result model are absent.

- [ ] **Step 3: Implement the remote-only launcher and attended checkpoints**

  Reuse only generic owned-process/UIA techniques from the legacy suite; do not reference or launch Local composition. Require an operator confirmation record for each visible checkpoint, but derive process/hash/session/resource facts automatically. On abort, preserve sanitized diagnostics and retain uncertain resources for explicit cleanup rather than claiming PASS.

- [ ] **Step 4: Execute the attended GUI walkthrough**

  Run on the exact published x86 package against the owned HTTPS/SQL environment. Expected: every checkpoint explicitly accepted, zero skipped/not-executed, Desktop exits cleanly, host/request queue is absent, and SQL cleanup is verified.

- [ ] **Step 5: Commit UI harness and runbook**

  Run: `git add tests/MyDmsVn.Vps03.Acceptance.Tests docs/runbooks/vps-03-issue-8-acceptance.md && git commit -m "test(vps-03): add attended HTTPS Desktop acceptance"`

### Task 8: Orchestration, evidence, final verification and PR #6 update

**Files:**
- Create: `scripts/Invoke-Vps03Issue8Acceptance.ps1`
- Modify: `.gitignore`
- Modify: `docs/evidence/vps-03-desktop-http-cutover.md`
- Modify: `docs/testing/acceptance-matrix.md`
- Modify: `docs/roadmap/README.md`
- Modify: `README.md`
- Modify: `AI_CONTEXT.md`
- Modify: PR #6 body and Issue #8 comment through `gh` after final evidence exists.

**Interfaces:**
- Consumes: Tasks 1–7, the current Git HEAD/base SHA, installed build/IIS tooling, operator-approved SQL/TLS inputs, and attended confirmation.
- Produces: one command `pwsh -File scripts/Invoke-Vps03Issue8Acceptance.ps1`, ignored `TestResults/vps03-issue8/<head>/` raw evidence, reviewed committed evidence summary, and updated draft PR #6.

- [ ] **Step 1: Write failing orchestration-result tests**

  Invoke the script in `-PreflightOnly` and fixture-result modes from `Vps03AcceptanceEvidenceTests`; assert refusal of dirty/mismatched HEAD, missing final package hash, missing/empty TRX, skipped required cases, failed cleanup, and secret-shaped evidence.

- [ ] **Step 2: Run orchestration tests to verify RED**

  Expected: FAIL because the entry point does not exist.

- [ ] **Step 3: Implement the thin PowerShell entry point**

  Build/publish Desktop x86 and HttpHost from current HEAD with locked restore, write SHA/hash provenance, call the acceptance project once with stable results paths, validate TRX/JSON through the C# evidence validator, and print counts plus artifact paths only. Do not embed acceptance logic or credentials in PowerShell.

- [ ] **Step 4: Run final local verification on the exact code HEAD**

  Run the issue runner; all four existing SQL-free suites; `python scripts/verify-vps-foundation.py`; `python scripts/verify-vps-01.py`; locked restore/build/list-tests for all discovery suites; and the exact Desktop x86 publish/dependency audit commands from `.github/workflows/vps-03-desktop-artifact.yml`.

  Expected: record actual SHA, environment, discovered/executed/skipped/failed counts per suite, schema fingerprints, cleanup, and artifact hashes. Any failure or required skip stays explicit and prevents Issue #8/VPS-03 completion.

- [ ] **Step 5: Commit truthful evidence and rerun final-HEAD CI**

  Update evidence/acceptance/roadmap only to the status actually proven. Commit with `docs(vps-03): record Issue 8 acceptance evidence`, push `codex/vps-03-desktop-http-cutover`, then wait for all PR #6 workflows on the exact final documentation HEAD. Record run URLs and counts in the PR body without creating a self-referential false SHA claim.

- [ ] **Step 6: Update PR #6 and Issue #8**

  Keep PR #6 draft and Issue #8 open if any real-environment or attended gate is incomplete. Close Issue #8 or mark VPS-03 complete only when the complete matrix and final-HEAD CI pass with zero required skips and no Owner exception is being inferred.

