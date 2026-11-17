# Issue #7 — UNKNOWN write recovery (VPS-03, operator runbook)

**Status (17 November 2026):** code `4a16c3a` verified on Windows CI (18/18 jobs; 1,296/1,296 SQL-free tests). This is **not** production acceptance. **No automatic replay.** This runbook is scoped to this repository and the exact deployed origin, schema, and commit. Maintain PR #6 as Draft until independent Windows GUI + HTTPS + SQL evidence and an Owner decision on any exceptions.

## Durable owner boundary

- The production Windows net48 adapter uses `WindowsHttpPendingWriteStore`. A single pending record is keyed by SHA-256 of the canonical HTTPS origin and Windows owner SID, under `%LOCALAPPDATA%\MyDmsVn-Vps\PendingWrites\`. Both the directory and file have protected owner/SYSTEM-only ACL; contents are DPAPI CurrentUser-encrypted with owner/origin-specific entropy.
- A bounded v1 record contains **only** UTC timestamp, actor numeric ID, protocol, kind, HTTP method, route, target ID, canonical deployment epoch (when journalled), UUIDv4 operation ID, and SHA-256 fingerprint. It MUST NOT contain command JSON, password, token, refresh cookie, connection string or decrypted response.
- `Save` writes a flushed temporary file and then performs a no-overwrite same-volume rename **before** the first HTTP write. A second Desktop process cannot overwrite that slot. A corrupt, inaccessible, ACL-weakened, copied-across-origin or unexpected-format record must fail closed; never delete it to work around startup admission.
- Only an exact known HTTP success or a freshly authenticated, envelope-validated WEB4/WEB5 COMMITTED result can remove a journalled fence. No 401/403/404/409/500/503, timeout, cancellation, session refresh or missing status is a negative certificate. Result expiry/unavailability retains UNKNOWN.
- New operations always generate a fresh key. There is no same-key resend, even after a valid `NOT_EXECUTED` certificate.

## Attended Desktop workflow

1. Sign in with a currently active session **as the original actor**. A different application user cannot inspect or clear that actor's operation. The session does not need to be the original in-memory Desktop session; the server will independently validate the *new* current `WebAuthSessions` row and current grants under SQL locks. This does **not** bypass session authorization. Logout, refresh rotation, revoked grant, epoch change and different actor can prevent readback.
2. Open the shell's **UNKNOWN write — review** status link (shown automatically on entering a shell with pending state). Copy the *exact* operation ID, actor, deployment epoch, kind, method, target, route and fingerprint into the support case. Never copy secrets or full command payloads.
3. Use **Check server status** (read-only). WEB4 uses `GET /api/v1/operations/{uuid}`; WEB5 uses `GET /api/v2/operations/{uuid}/resolution`. Both pass the exact epoch and fingerprint headers. Compare all envelope metadata, including actor/method/scope/target and WEB5 contract. An exact retained **COMMITTED** result is a positive settlement; **NOT_EXECUTED** is accepted only when WEB5 returns an exact terminal audit tuple and the user separately confirms it, with another fresh read at confirmation time.
4. Following COMMITTED, refresh document, ledger and stock from the server; do not restore a stale editor snapshot or reconstruct line IDs from a GET. Following certified NOT_EXECUTED, obtain a fresh authorized baseline and create a **new** intent with a new ID; the Desktop never resends the old command.
5. For **RESULT_EXPIRED, RESULT_UNAVAILABLE, INDETERMINATE, 404, 403, session mismatch, epoch mismatch or network errors**, preserve the UNKNOWN fence and escalate with immutable identifiers. Do not clear or rotate the owner record, infer rollback or bypass UI write admission.

## Operator investigation: WEB4/WEB5

WEB4/WEB5 original reads are actor-scoped and check current active session, deployment epoch, fingerprint and permission derived from stored operation kind. A newly authenticated session for the same user **can** read if its SQL session and original Write/Post grant are valid; this is not an exemption for another actor, an inactive user or revoked permissions.

Schema 13 already provides two separately privileged, append-only SQL procedures:

- `dbo.InspectWebOperationResolution`: run under **MyDmsVnWebOperationOperatorRole** in one explicit transaction, supplying original `@epoch`, `@actor`, `@operation`, `@hash`. It serializes with the original transaction's epoch and actor/key app-lock. `INDETERMINATE` or an absent row is **not** proof of NOT_EXECUTED.
- `dbo.RecordWebOperationNotExecuted`: only after independently established negative evidence for a *never-recorded* operation, use a distinct operator database login, exact immutable identity, UUIDv4 decision ID, bounded case ID, reason and evidence reference. The procedure refuses any existing journal or audit disposition; records audit and CANCELLED atomically in the operator's transaction. Commit once, then **independently reread**; a lost operator ACK does not prove success. A matched WEB5 `terminal` is then visible to the original actor with current authorization. The normal web runtime role is expressly denied operator authority.

Never grant the operator SQL role to the HTTP runtime principal, add a general operator HTTP endpoint, use table DML to cancel operations, mutate the original journal or produce a client-local fake certificate. If the original actor cannot reauthenticate, original permissions have been revoked, the epoch changed, or retained results have expired, **there is no generally authorized Desktop unlock in this version**. Keep the protected record, preserve its backup under the Windows owner, and require a separately designed and reviewed recovery contract plus an Owner decision.

## Catalog / Security mutations

These routes have no WEB4/WEB5 operation journal. They now persist an owner-scoped metadata-only record before write, and any ambiguous response blocks all subsequent writes across features. For existing target IDs, Catalog authorized GET readback is offered; Security authorized Users/Roles readback is offered. Successful GET merely proves present server state, **not** that the original mutation committed or rolled back. Catalog Create has no target ID, and permissions may deny readback.

The original request body is intentionally **not** persisted. Therefore client-only comparison cannot establish exact effects from a hash. The Desktop reports `READBACK_OBSERVED` while retaining UNKNOWN. A separate **audited, authorized reconciliation/release contract** for unjournalled writes must be agreed and implemented; until then, operator inspection/evidence cannot automatically clear this fence. Do not add a local "force clear" UI or equate a target GET with exact original write proof.

## Required independent acceptance before Issue #7 closure

| Category | Expected oracle |
| --- | --- |
| Windows process crash before/after durable rename | Before rename: no dispatch; after rename: exactly one pending key survives restart |
| Cancellation, lost ACK before/after SQL commit, 500/503/404/timeout | UNKNOWN retained; zero automatic business retries |
| Same actor, fresh login, refresh rotation, revoked session/grant, different actor | Current session/grant checked at every status read; no cross-actor resolution |
| Deployment epoch mismatch, fingerprint/key mismatch, copied/corrupt file | Fail closed; no inferential release |
| COMMITTED with exact result ID, RESULT_EXPIRED, RESULT_UNAVAILABLE, INDETERMINATE | Only exact COMMITTED releases positively; other states stay fenced |
| WEB5 audited NOT_EXECUTED | Exact terminal decision checked again, attended confirmation, fresh baseline/new key |
| Catalog/Security authoritative readback and denied permissions | Readback never alone releases UNKNOWN |
| Two clients with real SQL, ledger/stock/COGS, operator terminal race | Single-posted SQL effects; independent cross-process and operator transaction evidence |

Mock tests, compile/discovery and static published binary audit **do not** prove cross-process crash consistency or terminal authority for unjournalled writes. Issue #8 separately proved the exact published Desktop → HTTPS → SQL path, deterministic lost-ACK COMMITTED recovery, attended GUI and physical stock/COGS at `4c0b1be`; it did not execute process kills at Issue #7 file-save boundaries. Record each run's SHA, OS, DB fixture, timestamps, IDs and SQL oracles in `docs/evidence/`. Production remains gated until independent Issue #7 evidence and explicitly dated Owner exceptions, if any.
