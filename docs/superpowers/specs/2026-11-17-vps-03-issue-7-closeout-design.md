# VPS-03 Issue #7 Closeout Design

## Intent

Close Issue #7 with independent evidence that the existing restart-safe UNKNOWN
write design survives real Windows process termination, preserves its owner and
origin security boundary, never replays a business mutation, and continues to
enforce current actor/session/grant authority during recovery. Update draft PR
#6 without weakening authorization or claiming production readiness.

## Selected approach

Keep unjournalled Catalog/Security outcomes permanently fail-closed when no
terminal server proof exists. Authorized readback is evidence for an operator,
not a release certificate. Do not add a force-clear control, a general operator
HTTP endpoint, persisted request bodies, or a new terminal-release contract.

Two alternatives were rejected:

1. Close Issue #7 from Issue #8 plus SQL-free mocks. This leaves the explicit
   process-kill, file-corruption and operator-race gates unexecuted.
2. Add an audited Catalog/Security terminal-release contract. This changes the
   security model and requires a separate Owner decision; it is outside the
   issue's original rule to preserve UNKNOWN when proof is insufficient.

## Goals

- Prove the production `WindowsHttpPendingWriteStore` across real process
  boundaries under the current Windows owner.
- Prove a crash before durable admission cannot dispatch, while a crash after
  the atomic rename leaves exactly one recoverable intent.
- Prove malformed, copied-across-origin and ACL-weakened records fail closed.
- Prove a restarted client cannot resend and can release only through an exact
  COMMITTED result or an attended, freshly re-read WEB5 NOT_EXECUTED terminal.
- Exercise current-session and current-grant checks with the same actor, a
  different actor, revoked/restored permission and session rotation.
- Exercise the SQL operator terminal race with the restricted operator role and
  show one append-only disposition wins without business side effects.
- Reuse the Issue #8 ledger/stock/COGS oracle to demonstrate single posting.
- Record exact SHA, commands, environment, counts, cleanup and final-head CI.

## Non-goals and anti-goals

- No automatic retry or same-key resend.
- No assumption that 404, timeout, 500/503, result expiry or INDETERMINATE means
  NOT_EXECUTED.
- No cross-actor recovery, authorization bypass or grant snapshotting.
- No local deletion/rotation of an unresolved Catalog/Security fence.
- No production rollout, merge approval, Linux evidence or VPS-09 sign-off.
- No secrets, request bodies, tokens, cookies or connection strings in evidence.

## Test architecture

### Windows crash helper

Add a small net48 x86 helper executable owned by the VPS-03 acceptance suite.
It references the production HTTP adapter and supports only bounded test modes:

- `before-save`: signal readiness before calling `Save`; the parent terminates
  the process and proves no final intent exists and no dispatch marker exists.
- `after-save`: call the real production `Save`, signal only after the atomic
  rename returns, then wait. The parent terminates the process, starts a fresh
  process/store, and proves the exact operation ID/fingerprint survives.
- `load`: construct a new production store and return sanitized record identity
  and the failure code only; never emit protected bytes or credentials.

The parent acceptance test derives a unique HTTPS origin, owns the child PID,
uses bounded coordination files under ignored `TestResults`, verifies exact
paths before cleanup, and fails if the child exits or advances unexpectedly.
This tests the two meaningful dispatch-barrier sides without adding a production
crash hook inside `Save`.

### Real file-security cases

Using an origin unique to the run and the exact file created by the helper:

- replace ciphertext with bounded corrupt bytes and require constructor/load to
  fail with `Desktop.PendingIntentCorrupt`;
- add a non-owner allow rule and require ACL validation to fail closed;
- copy ciphertext into another origin's derived slot with owner-only ACL and
  require the different DPAPI entropy binding to fail closed;
- prove an exact matching record alone can be cleared.

Every destructive cleanup is limited to the exact run-owned files after path,
origin hash, count and parent-directory validation.

### Live HTTPS/SQL recovery cases

Extend the opt-in VPS-03 acceptance project and its manifest:

1. Persist an exact pending Sales/Goods identity, lose the ACK after the real
   host commits, terminate the first client process, and restart with the same
   Windows owner.
2. Before status read, verify all writes are fenced and no business request is
   sent. A different actor must not issue the status request.
3. With the original actor, revoke the original Write/Post grant and prove the
   status read cannot release. Restore the grant through the independently
   authorized fixture, authenticate a fresh session, and require exact
   COMMITTED metadata/result matching before release.
4. Re-run the stock/COGS snapshot and prove exactly one business effect.
5. For a never-dispatched WEB5 key, use the restricted operator SQL role in two
   concurrent transactions. Exactly one audited NOT_EXECUTED disposition may
   commit; the other must fail. Desktop inspection does not release, explicit
   attended confirmation performs a fresh read, then clears and requires a new
   key for any future operation.
6. Exercise missing, expired, unavailable, epoch/fingerprint/key mismatch and
   cancellation outcomes; every non-terminal or mismatched result stays fenced.
7. Exercise Catalog/Security ambiguous writes and authorized/denied readback;
   both outcomes retain the protected fence by design.

### Native UI checkpoint

Reuse the attended Native harness to open the UNKNOWN recovery dialog on the
exact published package. Observe the immutable identity fields, warning text,
read-only status action, disabled write surfaces, and the two-stage WEB5
NOT_EXECUTED confirmation. No Catalog/Security release action may exist.

## Evidence and orchestration

Add an Issue #7 mode/result record to the existing strict runner rather than
labeling SQL-free tests as acceptance. The full closeout requires:

- clean exact code/package HEAD;
- non-empty crash/file-security/live SQL/Native groups with zero required skips;
- exact process, file, IIS and SQL cleanup;
- the existing four SQL-free suites, discovery floors and x86 publish audit;
- a committed evidence summary and final status-only HEAD CI;
- final SHA and workflow URLs in PR #6 to avoid self-referential documentation.

Any missing operator SQL role, attended checkpoint, cleanup uncertainty or
unresolved test-owned intent is NOT PASS and keeps Issue #7 open.

## Status after successful closeout

Issue #7 may close when every gate above and final-head CI pass. PR #6 remains
Draft until independent code review and merge. VPS-03 may move only to the
status allowed by the roadmap; Issue #7 closure alone is not production
approval or integrated release acceptance.
