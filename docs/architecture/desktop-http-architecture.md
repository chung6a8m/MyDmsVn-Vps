# Desktop HTTP architecture — VPS-03

**Stage: IN PROGRESS**; see [VPS-03 evidence](../evidence/vps-03-desktop-http-cutover.md).

Desktop.App (Windows net48) references Desktop.WinForms and Desktop.Infrastructure.Http. The HTTP adapter uses Desktop.Application and Contracts, calls HttpHost over HTTPS /api/v1/*, with SQL Server behind HttpHost. The legacy Local adapter remains only for test compatibility. The Server net10/Linux transition occurs in VPS-04, not VPS-03.

Required variables: MYDMSVN_DESKTOP_API_ORIGIN=https://api.example.com and MYDMSVN_DESKTOP_OPERATION_MODE=web5, web4, or legacy-web11, matching the actual server mode. No SQL connection string, insecure TLS or implicit mode fallback is accepted in the Desktop.

The adapter retains Bearer in memory, refresh cookie in CookieContainer, serializes refresh, fences stale session generations, adds Origin to unsafe calls and X-Requested-With to cookie auth, and disables redirect/automatic retries.

Six Sales/Goods writes use explicit strict wire projections and WEB4-v1 SHA256 with epoch/idempotency key in WEB4/5. Unknown responses keep a pending fence; only authoritative COMMITTED status can resolve. Pending recovery is currently in-memory only and cannot be considered accepted across restarts.

See [VPS-02 API parity matrix](../contracts/http-api-parity.md) and [evidence](../evidence/vps-03-desktop-http-cutover.md); E2E SQL, binary audit and final-HEAD CI remain mandatory.
