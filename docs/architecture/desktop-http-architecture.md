# Desktop HTTP architecture — planned contract

**Stage:** PROPOSED for VPS-02/VPS-03; no such parity is claimed by VPS-00B.

Desktop net48 will use a typed HTTPS client with a replaceable transport layer. Features: Catalog/Product/Warehouse/Employee/Customer, Sales, Goods Receipt/Inventory, Security Administration, login/session and permission-aware lookups.

## Transport ownership

- One base URL and TLS policy per client configuration; no SQL connection string or local fallback in production after cutover.
- Bearer access token, refresh-token rotation, explicit logout, expired-session handling; never log credentials or tokens.
- Central mapping for non-2xx HTTP, validation, concurrency conflict, permission denied and timeouts; preserve desktop interaction semantics.
- Cancellation tokens and per-operation retry **only when proven safe/idempotent**. Never automatically retry transactional Post/ledger commands.
- DTO/wire adapters isolate Desktop Newtonsoft.Json from later Server System.Text.Json, including decimal, int64, date/time, null and optional fields.
- Transition matrix enumerates every Desktop API interface/operation and maps it to endpoint + scenario + test/evidence owner (see [parity register](../contracts/http-api-parity.md)).

VPS-03 acceptance requires Desktop -> HTTPS -> Host -> real SQL E2E on Windows client, not simply successful compilation or localhost-mocked responses. Keep Local adapter available solely during transition; remove from production composition after approval.
