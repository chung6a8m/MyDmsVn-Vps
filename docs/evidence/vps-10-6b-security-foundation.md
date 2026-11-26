# VPS-10.6B — Native Security and Foundation evidence

Base: `8b07a7c491b6fde0e1623e5bfcbe6fcd4baf26e6` (merged PR #41). Implementation [PR #42](https://github.com/chung6a8m/MyDmsVn-Vps/pull/42). Source conversion: `718654d6b30ea276dde7e75dc68e7cc421023b03`.

## Source scope

- Eight existing routes: seven Security and one Foundation. Expected 44 native MVC / 13 bridge / 57 total, unchanged route identities and desktop feature gate.
- Explicit typed query and request DTOs, strict allowlists, numeric Int64 revisions (including 9007199254740993), no string or double conversion. Reuse Application commands, trusted request identity, permission checks, error envelopes and response contracts. No client-supplied authorized actor.
- Native endpoint inventory, OpenAPI integer/int64 request schemas, and frozen differential corpus remain checked. No migration, Desktop target, production SQL principal or authentication-policy change.
- Fifty focused HTTP cases: 8 admission, 9 independent permissions, 30 malformed wire/actor spoof cases and 3 cultures. Exact count enforced by Linux TRX gate.
- One disposable HTTPS/SQL fact: independent catalog/role principals, actor spoof and self-grant denial, role mutation, assignment/import, stale revision rejection, persisted assignment revision and audit actor. CI-only bootstrap adds two synthetic users and three explicit SELECT-only oracle tables (Roles, RolePermissions, SecurityAuditEvents); Users remains denied and production runtime permissions unchanged. Exact count enforced by SQL TRX gate.

## Evidence boundary

| Gate | Required evidence | Current status |
|---|---|---|
| G1 | Linux native build, route inventory, OpenAPI, 50/50 focused HTTP, no failures or skips | CI REQUIRED — not verified |
| G2 | All 293 frozen differential cases unchanged and PASS | CI REQUIRED — not verified |
| G3 | Desktop net48 regression / Windows CI | CI REQUIRED — not verified |
| G4 | Disposable restricted-principal HTTPS/SQL and 1/1 security audit test, no failures or skips; existing SQL acceptance retained | CI REQUIRED — not verified |

Local checkout, .NET SDK and SQL runner unavailable in implementation environment. No local build/test execution or runtime PASS is claimed. Exact final HEAD and CI receipts must be checked in PR #42 by independent review before merge. No production deployment or release approval.
