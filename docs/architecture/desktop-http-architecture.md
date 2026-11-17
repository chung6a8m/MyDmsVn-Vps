# Desktop HTTP architecture — VPS-03

**Stage: IN PROGRESS — Issue #8 accepted; Issue #7, review and merge open**; see [VPS-03 evidence](../evidence/vps-03-desktop-http-cutover.md).

Desktop.App (Windows net48) references Desktop.WinForms and Desktop.Infrastructure.Http. The HTTP adapter uses Desktop.Application and Contracts, calls HttpHost over HTTPS /api/v1/*, with SQL Server behind HttpHost. The legacy Local adapter remains only for test compatibility. The Server net10/Linux transition occurs in VPS-04, not VPS-03.

Required variables: MYDMSVN_DESKTOP_API_ORIGIN=https://api.example.com and MYDMSVN_DESKTOP_OPERATION_MODE=web5, web4, or legacy-web11, matching the actual server mode. No SQL connection string, insecure TLS or implicit mode fallback is accepted in the Desktop.

The adapter retains Bearer in memory, refresh cookie in CookieContainer, serializes refresh, fences stale session generations, adds Origin to unsafe calls and X-Requested-With to cookie auth, and disables redirect/automatic retries.

Six Sales/Goods writes use explicit strict wire projections and WEB4-v1 SHA256 with epoch/idempotency key in WEB4/5. **Issue #7 implementation candidate:** before dispatch, a bounded metadata-only intent is persisted under a Windows-owner-scoped DPAPI-protected file with restricted ACL and atomic no-overwrite rename. Startup and all Catalog/Security/Sales/Goods write clients share a fail-closed fence across restarts. The backend's original WEB4/WEB5 actor/session/Write/Post grants remain mandatory; the Desktop never resends a pending operation. An exact retained COMMITTED result is the only positive journal resolution; an exact WEB5 audited NOT_EXECUTED certificate requires attended confirmation and fresh baseline. Catalog/Security readback is advisory and cannot automatically clear a fence because these APIs have no journal. See [Issue #7 operator runbook](../runbooks/issue-7-unknown-write-recovery.md).

Issue #8 exact-SHA Windows acceptance now proves the published Desktop/HTTPS/SQL path, attended GUI, exact stock/COGS single-post and deterministic lost-ACK COMMITTED recovery. **Still not accepted:** Windows process-crash durability at file-save boundaries and a terminal authority for unjournalled Catalog/Security writes. External operator investigation, session/epoch loss and unjournalled writes can remain permanently UNKNOWN without a separately reviewed recovery authority.

See [VPS-02 API parity matrix](../contracts/http-api-parity.md) and [evidence](../evidence/vps-03-desktop-http-cutover.md); final documentation-HEAD CI, independent review and Issue #7 acceptance remain mandatory.
