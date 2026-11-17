# VPS-03 Issue #7 Closeout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close Issue #7 with exact-HEAD evidence that restart-safe UNKNOWN writes survive real Windows process termination, remain owner/origin scoped, never replay, and release only through the existing authorized terminal paths.

**Architecture:** Add a narrow net48 x86 child process that exercises the production `WindowsHttpPendingWriteStore` across actual process boundaries, then extend the existing opt-in VPS-03 acceptance suite for file security, live HTTPS/SQL recovery, operator terminal races, and the Native recovery dialog. Keep production behavior fail closed: exact COMMITTED or attended fresh WEB5 NOT_EXECUTED may clear a journalled fence, while Catalog/Security readback never clears it.

**Tech Stack:** .NET Framework 4.8, C# 12, xUnit v3, WinForms/UI Automation, `WindowsHttpPendingWriteStore`, DPAPI/Windows ACLs, IIS Express HTTPS, SQL Server, PowerShell, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-17-vps-03-issue-7-closeout-design.md`

## Global Constraints

- Preserve authentication, permissions, SQL transaction semantics, inventory/costing, idempotency, JSON wire behavior, and schema compatibility.
- Do not resend or replay a pending business write and do not persist request bodies, credentials, cookies, tokens, SQL connection strings, or other secrets.
- Do not infer NOT_EXECUTED from cancellation, timeout, 404, 500/503, missing/expired results, INDETERMINATE, epoch mismatch, fingerprint mismatch, or actor/session mismatch.
- Require the current original actor and current Write/Post grant for journal recovery; never snapshot authority or permit cross-actor recovery.
- Keep Catalog/Security UNKNOWN permanently fenced when no terminal server proof exists; do not add force-clear UI, a general operator HTTP endpoint, or a new release contract.
- Run live SQL only against the operator-designated non-production connection and record missing prerequisites as NOT RUN, never PASS.
- Every result must bind to the exact clean HEAD and record discovered/executed/skipped/failure counts, environment, cleanup, and CI URLs without printing secrets.
- Maintain net48 x86 Desktop compatibility and do not retarget production projects or remove the Local adapter in VPS-03.

## Review Focus

- A child killed before `Save` must leave no final intent or dispatch marker; `Vps03PendingStoreProcessTests.Crash_before_save_leaves_no_admitted_intent_or_dispatch` owns this case.
- A valid ciphertext copied to another canonical origin must fail DPAPI entropy validation even with an owner-only ACL; `Vps03PendingStoreSecurityTests.Ciphertext_copied_to_another_origin_fails_closed` owns this case.
- A terminal response with any actor, epoch, operation, fingerprint, kind, method, target, scope, or contract mismatch must retain the fence; `Vps03UnknownRecoveryAcceptanceTests.Mismatched_or_nonterminal_results_never_release_the_fence` owns this case.
- Concurrent operator terminal decisions must produce exactly one audit disposition, and a lost operator ACK must require an independent reread; `Vps03OperatorRecoveryAcceptanceTests.Concurrent_operator_dispositions_have_one_winner_and_require_reread` owns this case.
- Authorized or denied Catalog/Security readback must show no release control and retain the same pending identity; `Vps03UnjournalledRecoveryAcceptanceTests.Catalog_and_security_readback_remain_permanently_fenced` and the Native checkpoint own this case.

---

### Task 1: Real process-boundary pending-store helper

**Files:**
- Create: `tests/MyDmsVn.Vps03.PendingStore.CrashHost/MyDmsVn.Vps03.PendingStore.CrashHost.csproj`
- Create: `tests/MyDmsVn.Vps03.PendingStore.CrashHost/Program.cs`
- Create: `tests/MyDmsVn.Vps03.PendingStore.CrashHost/packages.lock.json`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03PendingStoreProcess.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03PendingStoreProcessTests.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj`
- Modify: static project inventory/count expectations found by `python scripts/verify-vps-foundation.py`

**Interfaces:**
- Consumes: `WindowsHttpPendingWriteStore(HttpDesktopOptions)`, `Save(HttpPendingWriteRecord)`, and `Load()` from the production HTTP adapter.
- Produces: child CLI modes `before-save`, `after-save`, and `load`; `Vps03PendingStoreProcess.Start(mode, origin, exchangeDirectory, record)` returning an owned process whose bounded exchange contains only stage, operation ID, fingerprint, and sanitized failure code.

- [ ] **Step 1: Add failing parent tests for both crash boundaries**

  Add `Crash_before_save_leaves_no_admitted_intent_or_dispatch` and `Crash_after_atomic_save_is_loaded_by_a_fresh_process`. The first must wait for `before-save-ready`, kill the owned PID, and assert no final slot and no dispatch marker. The second must wait for `after-save-complete`, kill the PID, launch `load`, and assert exact operation ID/fingerprint and one final slot.

- [ ] **Step 2: Run the focused tests and verify RED**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter FullyQualifiedName~Vps03PendingStoreProcessTests`

  Expected: FAIL because the crash host/process wrapper does not exist.

- [ ] **Step 3: Implement the smallest bounded crash host and parent wrapper**

  Target `net48`, `x86`, and reference only the production HTTP adapter. Signal before invoking `Save` in `before-save`; signal only after `Save` returns in `after-save`; wait indefinitely for the parent after signaling. In `load`, emit only a sanitized bounded JSON record. The parent must validate absolute run-owned paths, PID ownership, stage ordering, timeout, executable architecture, and exact cleanup targets.

- [ ] **Step 4: Restore, run the focused tests, and verify GREEN**

  Run: `dotnet restore tests/MyDmsVn.Vps03.PendingStore.CrashHost/MyDmsVn.Vps03.PendingStore.CrashHost.csproj --use-lock-file; dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter FullyQualifiedName~Vps03PendingStoreProcessTests`

  Expected: two tests executed and passed, zero failed/skipped, both child trees absent, and the run-owned intent removed after assertions.

- [ ] **Step 5: Run the foundation inventory verifier and update only exact derived counts**

  Run: `python scripts/verify-vps-foundation.py`

  Expected: PASS with the new helper classified as a test utility, no missing relative project references, and no production dependency-boundary regression.

- [ ] **Step 6: Commit the process-boundary helper**

  Commit: `test(vps03): exercise pending store across process crashes`

### Task 2: Production file-security and corruption acceptance

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03PendingStoreSecurityTests.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03PendingStoreFixture.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03PendingStoreProcess.cs`
- Modify: `tests/MyDmsVn.Desktop.Session.Tests/Vps03PendingRecoveryTests.cs`

**Interfaces:**
- Consumes: Task 1 child modes and production owner/origin slot derivation.
- Produces: `Vps03PendingStoreFixture` with exact run-owned `Origin`, `IntentPath`, `SaveInChild`, `LoadInChild`, `ReplaceCiphertext`, `WeakenAcl`, `CopyToOrigin`, and validated exact cleanup.

- [ ] **Step 1: Add failing security tests**

  Add `Bounded_corrupt_ciphertext_fails_closed`, `Owner_file_with_weakened_acl_fails_closed`, `Ciphertext_copied_to_another_origin_fails_closed`, and `Clear_removes_only_the_exact_matching_record`. Assert `Desktop.PendingIntentCorrupt` for corrupt/ACL/origin failures and `Desktop.PendingWriteChanged` for nonmatching clear.

- [ ] **Step 2: Run the focused tests and verify RED**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter FullyQualifiedName~Vps03PendingStoreSecurityTests`

  Expected: FAIL because the fixture and real mutation cases do not exist.

- [ ] **Step 3: Implement exact-path file mutations and fail-closed assertions**

  Require canonical unique HTTPS origins, the expected SHA-256 slot name, owner/SYSTEM-only parent validation, one final intent at most, bounded replacement bytes, and a deliberately added non-owner allow rule. Restore/cleanup only the exact files created by the fixture and refuse cleanup outside `%LOCALAPPDATA%\MyDmsVn-Vps\PendingWrites`.

- [ ] **Step 4: Add/retain unit pinning for record bounds and identity comparison**

  Extend `Vps03PendingRecoveryTests` only where a production invariant is not already pinned; do not duplicate real ACL/process tests with mocks.

- [ ] **Step 5: Run focused acceptance and Desktop.Session tests**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter FullyQualifiedName~Vps03PendingStoreSecurityTests; dotnet test tests/MyDmsVn.Desktop.Session.Tests/MyDmsVn.Desktop.Session.Tests.csproj -c Release -f net48 --no-restore`

  Expected: four real file-security cases pass; Desktop.Session meets its updated non-empty count with zero failures/skips.

- [ ] **Step 6: Commit file-security acceptance**

  Commit: `test(vps03): prove pending intent file security`

### Task 3: Live HTTPS/SQL restart, authority, and exactly-once recovery

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03UnknownRecoveryAcceptanceTests.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03RecoveryClientProcess.cs`
- Modify: `tests/MyDmsVn.Vps03.PendingStore.CrashHost/Program.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03LostAckProxy.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03OwnedEnvironment.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03SqlOracle.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03TransactionAcceptanceTests.cs`

**Interfaces:**
- Consumes: Tasks 1–2 durable slot/helper, existing isolated schema-13 IIS/SQL fixture, lost-ACK handler, and stock/COGS snapshot oracle.
- Produces: child modes to execute a bounded named Sales/Goods scenario without exposing payload or credentials; fixture methods to revoke/restore the exact current grant, rotate session, capture request counts, and read sanitized ledger/stock/COGS facts.

- [ ] **Step 1: Add failing live recovery tests**

  Add `Lost_ack_commit_survives_client_kill_and_releases_only_for_current_authorized_actor`, `Different_actor_and_revoked_grant_cannot_inspect_or_release`, `Session_rotation_with_restored_grant_can_release_exact_committed_result`, and `Mismatched_or_nonterminal_results_never_release_the_fence`. Assert zero resend, one server mutation, same durable identity after restart, explicit current authorization, and retained fence for cancellation, timeout, missing/expired/unavailable result, 404, 500/503, INDETERMINATE, and every identity mismatch.

- [ ] **Step 2: Run the live filter and verify RED or explicit NOT RUN**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter Category=Vps03Issue7Live`

  Expected with prerequisites: FAIL because cross-process live modes are missing. Without prerequisites: stop and record BLOCKED/NOT RUN; do not treat skip or empty discovery as PASS.

- [ ] **Step 3: Implement the bounded live child protocol and authority controls**

  Pass secrets only through the existing scrubbed child environment, never through exchange/evidence files or command-line arguments. Persist before dispatch, drop the committed response, terminate the first owned client, and start a fresh client using the same Windows owner. Instrument business request/status request counts without changing production dispatch.

- [ ] **Step 4: Implement live grant/session and SQL oracle operations**

  Revoke and restore only the test-owned user's original Write/Post grant using the independently authorized fixture. Require fresh login/session after restoration. Snapshot ledger count, valuation count, balance quantity/value, and COGS before and after recovery and assert exact equality after the initial commit.

- [ ] **Step 5: Run live recovery and existing transaction acceptance**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter "Category=Vps03Issue7Live|CaseId=VPS03.GATE.LOST-ACK|CaseId=VPS03.GATE.INVENTORY"`

  Expected: non-empty execution, all passed, zero skipped; one mutation and unchanged single-post ledger/valuation/stock/COGS after recovery; verified IIS, SQL, process, and intent cleanup.

- [ ] **Step 6: Commit live restart recovery**

  Commit: `test(vps03): prove authorized restart recovery`

### Task 4: Operator race and permanent unjournalled fence

**Files:**
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03OperatorRecoveryAcceptanceTests.cs`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03UnjournalledRecoveryAcceptanceTests.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03OwnedEnvironment.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03SqlOracle.cs`
- Modify: `tests/MyDmsVn.Server.Infrastructure.Security.Tests/WebOperationMaintenanceOperatorTests.cs`

**Interfaces:**
- Consumes: `SqlWebOperationResolutionMaintenance`, schema-13 operator role/procedures, current Desktop recovery logic, and Task 3 live environment.
- Produces: a test-owned restricted operator login/connection, concurrent disposition runner, independent terminal reread, and live Catalog/Security readback assertions.

- [ ] **Step 1: Add failing operator race and unjournalled tests**

  Add `Concurrent_operator_dispositions_have_one_winner_and_require_reread`, `Runtime_principal_cannot_record_terminal_disposition`, and `Catalog_and_security_readback_remain_permanently_fenced`. The race must use two explicit operator transactions with distinct decision IDs; assert one audit/CANCELLED tuple, one rejected competitor, no business rows, and no Desktop release before a fresh reread plus attended confirmation.

- [ ] **Step 2: Run focused tests and verify RED or explicit NOT RUN**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter "FullyQualifiedName~Vps03OperatorRecoveryAcceptanceTests|FullyQualifiedName~Vps03UnjournalledRecoveryAcceptanceTests"`

  Expected with prerequisites: FAIL because the restricted live operator fixture is absent. Missing operator authority is BLOCKED/NOT RUN.

- [ ] **Step 3: Implement the restricted operator fixture and terminal race**

  Grant only `MyDmsVnWebOperationOperatorRole` to a unique owned login, verify it is not sysadmin/db_owner/runtime, execute both transactions concurrently, and independently inspect after commit/ACK loss. Drop the exact login during verified cleanup.

- [ ] **Step 4: Implement Catalog/Security live readback checks without release**

  Exercise one ambiguous Catalog and one Security write, then both authorized and denied readback. Assert `READBACK_OBSERVED` only for authorized readback, `UNKNOWN` for denial/error, `FenceReleased == false` throughout, identical persisted operation identity, and no clear/force-release path.

- [ ] **Step 5: Run acceptance plus existing operator security suite**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter "Category=Vps03Issue7Live"; dotnet test tests/MyDmsVn.Server.Infrastructure.Security.Tests/MyDmsVn.Server.Infrastructure.Security.Tests.csproj -c Release -f net48 --filter FullyQualifiedName~WebOperationMaintenanceOperatorTests`

  Expected: non-empty all-pass results with zero skips; exactly one operator disposition, no runtime privilege, no business side effect, and unresolved Catalog/Security intents preserved until exact fixture cleanup.

- [ ] **Step 6: Commit operator and unjournalled acceptance**

  Commit: `test(vps03): verify terminal authority and permanent fences`

### Task 5: Native UNKNOWN recovery checkpoint

**Files:**
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AttendedGuiAcceptanceTests.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03RemoteGuiProbe.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AttendedExchange.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/vps03-issue8-case-manifest.json`
- Modify: `docs/runbooks/issue-7-unknown-write-recovery.md`

**Interfaces:**
- Consumes: exact published x86 Desktop package, Task 3/4 live pending identities, existing attended checkpoint exchange, and `DesktopWriteRecoveryDialog` UI Automation surface.
- Produces: `VPS03.GUI.UNKNOWN-RECOVERY` attended checkpoint with sanitized observations for immutable identity, warning, write fence, read-only check, two-stage WEB5 confirmation, and absence of Catalog/Security release controls.

- [ ] **Step 1: Add failing attended-result and UI probe tests**

  Require `VPS03.GUI.UNKNOWN-RECOVERY` in the checkpoint set. Add probe assertions for visible operation ID/actor/epoch/kind/method/target/fingerprint, the no-retry warning, disabled write surfaces, `Check server status`, confirmation disabled before an exact terminal, and no force-clear/Catalog/Security release action.

- [ ] **Step 2: Run SQL-free attended contract tests and verify RED**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter "FullyQualifiedName~Vps03AttendedGuiAcceptanceTests&Phase!=Attended"`

  Expected: FAIL because the new checkpoint/probe state is not wired.

- [ ] **Step 3: Extend the Native probe and attended exchange**

  Drive only UI Automation against the exact published process. Seed/reuse a live pending case without injecting a fake local result. Record only field presence/matches, control state, executable hash/PID, decision, and timestamps; exclude credentials and payloads.

- [ ] **Step 4: Run the attended Native checkpoint**

  Run via the Issue #7 runner added in Task 6 with `-AttendedOnly`, then complete the operator checkpoint in the Native Desktop window.

  Expected: one non-skipped attended test passes with all required checkpoints accepted, two-stage WEB5 NOT_EXECUTED fresh reread observed, no Catalog/Security release action, clean Desktop/IIS exit, SQL cleanup, and intent cleanup/retention exactly matching the scenario.

- [ ] **Step 5: Commit Native recovery acceptance**

  Commit: `test(vps03): add native unknown recovery checkpoint`

### Task 6: Strict Issue #7 orchestration, manifest, and evidence

**Files:**
- Create: `scripts/Invoke-Vps03Issue7Acceptance.ps1`
- Create: `tests/MyDmsVn.Vps03.Acceptance.Tests/vps03-issue7-case-manifest.json`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceManifest.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceManifestTests.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceEvidence.cs`
- Modify: `tests/MyDmsVn.Vps03.Acceptance.Tests/Vps03AcceptanceEvidenceTests.cs`
- Modify: `tests/README.md`
- Modify: `docs/testing/acceptance-matrix.md`

**Interfaces:**
- Consumes: Tasks 1–5 tests/results, clean exact HEAD, Issue #8 package builder, and operator-supplied SQL/TLS inputs.
- Produces: strict Issue #7 modes `-PreflightOnly`, `-CrashOnly`, `-LiveOnly`, `-AttendedOnly`, and full aggregation; exact-HEAD JSON/Markdown evidence with per-group totals and cleanup facts.

- [ ] **Step 1: Add failing manifest/evidence tests**

  Require stable cases for crash-before-save, crash-after-save, corruption, ACL, origin binding, restart/actor/grant/session, terminal mismatches, operator race, Catalog/Security, Native UI, and cleanup. Reject empty groups, skipped/not-executed cases, SHA mismatch, duplicate IDs, secret-shaped fields, missing cleanup, or evidence imported from another HEAD.

- [ ] **Step 2: Run manifest/evidence tests and verify RED**

  Run: `dotnet test tests/MyDmsVn.Vps03.Acceptance.Tests/MyDmsVn.Vps03.Acceptance.Tests.csproj -c Release -f net48 --filter "FullyQualifiedName~Vps03AcceptanceManifestTests|FullyQualifiedName~Vps03AcceptanceEvidenceTests"`

  Expected: FAIL until the Issue #7 schema and strict groups exist.

- [ ] **Step 3: Implement the Issue #7 manifest and runner**

  Reuse package publication mechanics but write under `TestResults\vps03-issue7\<HEAD>`. Require a clean 40-hex HEAD, canonical HTTPS origin, explicit opt-in, non-empty tests for every selected group, exact TRX counts, zero skips/failures, child/IIS/SQL/intent cleanup facts, and exact matching external attended result for full aggregation.

- [ ] **Step 4: Implement sanitized Issue #7 evidence output**

  Use an Issue #7 title/schema, include base/head SHA, commands, Windows/.NET/IIS/SQL capability facts, group totals, process/file/SQL cleanup, package hashes, operator checkpoint decisions, and blockers. Continue rejecting secret-shaped keys and raw errors.

- [ ] **Step 5: Run SQL-free runner modes and evidence tests**

  Run: `pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -PreflightOnly; pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -CrashOnly`

  Expected: preflight and crash/file-security groups pass on a clean committed HEAD; non-empty exact totals, zero skips, exact cleanup, and evidence bound to that HEAD.

- [ ] **Step 6: Commit runner and evidence contracts**

  Commit: `test(vps03): add strict issue 7 acceptance runner`

### Task 7: Final exact-HEAD acceptance, documentation, CI, and GitHub closeout

**Files:**
- Modify: `docs/evidence/vps-03-desktop-http-cutover.md`
- Modify: `docs/architecture/desktop-http-architecture.md`
- Modify: `docs/runbooks/issue-7-unknown-write-recovery.md`
- Modify: `docs/roadmap/README.md`
- Create: `docs/evidence/vps-03-issue-7-closeout.md`
- Modify: `.github/workflows/vps-foundation.yml` only if the new helper/manifest requires a static/discovery assertion

**Interfaces:**
- Consumes: committed Tasks 1–6, operator-approved live environment and Native checkpoint, repository test policy, GitHub Issue #7, and draft PR #6.
- Produces: committed exact package-SHA evidence, final status-only HEAD CI evidence, an Issue #7 closure comment, and an updated still-Draft PR #6 description/comment.

- [ ] **Step 1: Run the full Issue #7 acceptance package on a clean committed HEAD**

  Run: `pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -AttendedResult <exact-path> -LiveResult <exact-path>` using only operator-approved environment variables.

  Expected: every crash/file/live/operator/Native group is non-empty and all passed; zero failed/skipped/not-executed; exact process/file/IIS/SQL cleanup; no unresolved test-owned intent; record the package HEAD and result paths.

- [ ] **Step 2: Run the four SQL-free suites and discovery/publish floors**

  Run the exact commands in `docs/testing/test-policy.md` and `tests/README.md`: Architecture, Desktop.Session, Application, and HttpHost execute on Windows net48; all required suites have non-empty `--list-tests`; publish Desktop Release net48 x86 and audit its PE architecture/dependencies.

  Expected: every required suite meets its current committed floor with zero failures/skips for execution suites; discovery is non-empty and at/above floors; x86 publish has no forbidden production Server/SQL references.

- [ ] **Step 3: Write and commit the evidence/status documentation**

  Record base SHA, package HEAD, exact commands/environment capabilities, discovered/executed/passed/failed/skipped counts, cleanup, package hashes, and explicit limitations. State that Catalog/Security can remain permanently UNKNOWN, Issue #7 closure is not production approval, and PR #6 remains Draft pending review/merge.

  Commit: `docs(vps03): record issue 7 closeout evidence`

- [ ] **Step 4: Run final status-only HEAD verification**

  Run: `git status --short; python scripts/verify-vps-foundation.py; node scripts/check-web2-wire-fixtures.mjs; dotnet test ...` for every SQL-free suite affected by the final documentation/static change.

  Expected: clean worktree and all local static/SQL-free checks pass on the documentation commit HEAD. Do not reuse pre-commit counts as final-HEAD evidence.

- [ ] **Step 5: Push the branch and wait for all PR #6 final-HEAD workflows**

  Expected: every required workflow/job completes successfully on the exact final HEAD. Record workflow names, run URLs, job counts, and the final 40-hex SHA; investigate and fix any failure, then rerun acceptance affected by the fix.

- [ ] **Step 6: Update PR #6 and close Issue #7**

  Update draft PR #6 with the exact acceptance package SHA, final HEAD, test counts, commands, environment, cleanup, CI URLs, and remaining review/merge blockers. Add the same concise evidence to Issue #7, explicitly note the permanent Catalog/Security fail-closed limitation, then close Issue #7 only after all gates above pass.

- [ ] **Step 7: Verify remote state**

  Expected: Issue #7 is CLOSED; PR #6 is OPEN and Draft on the exact final HEAD; all checks are green; roadmap/evidence claims match the committed documents; no merge or production-ready claim was made.
