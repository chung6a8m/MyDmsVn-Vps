# VPS-03 — Desktop HTTP Cutover

**Status: IN PROGRESS.** No production acceptance is implied by implementation commits.

Desktop.App net48 now uses a dedicated HTTP adapter, with no direct Local/Server/SQL references in its project or composition root. The legacy Local adapter is retained only for old tests. The vendored SourceGrid net48 target excludes SqlClient and the legacy SQL helper; the net8 vendor target remains unchanged.

Configuration requires MYDMSVN_DESKTOP_API_ORIGIN (canonical HTTPS origin) and MYDMSVN_DESKTOP_OPERATION_MODE (legacy-web11, web4, web5). On the host, additive Desktop endpoints require MYDMSVN_WEB_DESKTOP_API=enabled. No downgrade or non-HTTPS fallback is permitted.

Bearer remains in memory, refresh cookie in HttpClientHandler. Refresh is single-flight, session generations fence stale responses, login/refresh/logout use required same-origin headers. Writes are never retried automatically. Six WEB4/WEB5 Sales/Goods commands carry deployment epoch, v4 idempotency key, and WEB4-v1 SHA256. Lost ACK, 503 and timeouts retain an in-memory pending fence; only a matching committed journal outcome clears it.

**Still pending independent acceptance:** final-HEAD Windows net48 locked restore/build and tests; frozen wire vectors and session concurrency; actual published binary/dependency audit; Windows Desktop -> HTTPS Host -> real SQL E2E for all 46 API methods, stock/COGS and security; refresh/logout and restart recovery; GUI acceptance. In-memory pending recovery is not durable across process restart and is not signed off.

The authoritative status remains in [roadmap](../roadmap/README.md).
