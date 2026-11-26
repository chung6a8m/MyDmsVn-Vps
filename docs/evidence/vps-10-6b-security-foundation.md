# VPS-10.6B — Native Security and Foundation evidence

Base: `8b07a7c491b6fde0e1623e5bfcbe6fcd4baf26e6` (merged PR #41). Implementation [PR #42](https://github.com/chung6a8m/MyDmsVn-Vps/pull/42). Source conversion: `718654d6b30ea276dde7e75dc68e7cc421023b03`.

## Source scope

- Eight existing routes: seven Security and one Foundation. Expected 44 native MVC / 13 bridge / 57 total, unchanged route identities and desktop feature gate.
- Explicit typed query and request DTOs, strict allowlists, numeric Int64 revisions (including 9007199254740993), no string or double conversion. Reuse Application commands, trusted request identity, permission checks, error envelopes and response contracts. No client-supplied authorized actor.
- Native endpoint inventory, OpenAPI integer/int64 request schemas, and frozen differential corpus remain checked. No migration, Desktop target, production SQL principal or authentication-policy change.
- Fifty-nine focused HTTP cases: 8 admission, 9 independent permissions, 30 malformed wire/actor spoof cases, 8 error field/precedence cases, 1 nullable-ID case, and 3 cultures. Exact count enforced by Linux TRX gate.
- Two disposable HTTPS/SQL facts: independent catalog/role/user principals, actor spoof and self-grant denial, role mutation and import, user direct deny/import allow, stale revision rejection, persisted permissions and exact typed audit counts/actor. CI-only bootstrap adds four synthetic users and four explicit SELECT-only oracle tables (Roles, RolePermissions, SecurityAuditEvents, UserPermissions); Users remains denied and production runtime permissions unchanged. Exact count enforced by SQL TRX gate.

## Evidence boundary

| Gate | Required evidence | Current status |
|---|---|---|
| G1 | Linux native build, route inventory, OpenAPI, 59/59 focused HTTP, no failures or skips | CI REQUIRED — not verified |
| G2 | All 293 frozen differential cases unchanged and PASS | CI REQUIRED — not verified |
| G3 | Desktop net48 regression / Windows CI | CI REQUIRED — not verified |
| G4 | Disposable restricted-principal HTTPS/SQL and 2/2 security audit tests, no failures or skips; existing SQL acceptance retained | CI REQUIRED — not verified |

Local checkout, .NET SDK and SQL runner unavailable in implementation environment. No local build/test execution or runtime PASS is claimed. Exact final HEAD and CI receipts must be checked in PR #42 by independent review before merge. No production deployment or release approval.

## Independent review — initial HEAD `4dd6a4fb65463472e476645b6f13827f72c8a5ae`

- P1 (acceptance coverage): the original SQL gate only proved authorized Role changes; User mutations/import were denied-only or simulated. Add a distinct User manager and isolated synthetic subject; independently check direct Deny and imported Allow in SQL, observed permission decisions, preview non-mutation, stale revision and audit.
- P2 (audit oracle): Role audit predicates did not discriminate TargetType and the total was actor-filtered, allowing unexpected extra/wrong-actor rows to escape detection. Require exact Role target type, actor and total; verify User before/after outcome and exact totals separately.
- P2 (contract/schema): nullable optional mutation IDs were accepted by the legacy wire but advertised as non-nullable CLR ints. Keep nullable HTTP DTOs and map absent/null to the existing zero sentinel at the Application boundary. Assert schema plus runtime defaults, exact legacy error fields/precedence and numeric response revision.
- Initial HEAD: 11/11 PR workflows succeeded (protected staging/live/attended jobs skipped, not accepted). This is baseline evidence only. All review-fix HEAD gates require fresh CI; no local build or SQL execution claimed.
