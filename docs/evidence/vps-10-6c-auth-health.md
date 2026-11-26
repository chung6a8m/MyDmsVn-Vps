# VPS-10.6C — Native Auth/Health evidence

Base: `3eabcce20b42add6666788193146326a0b9b7da0` (merged PR #42). Branch: `vps-10.6c-native-auth-health`.

## Source scope (pending execution and review)

- Six MVC routes: login, refresh, logout, me, capabilities, health. Expected actual inventory 50 native / 7 bridge / 57 immutable logical routes. The old controllers remain solely as the transitional source-linked compatibility inventory until VPS-10.7; MVC owns all six at runtime.
- Typed login body, isolated strict UTF-8/string-only formatter, no retained credential DOM, original request-level validation envelope. Typed success responses, exact cookie flags/order/path and invariant expiry/Max-Age, existing rate limiter and session store. No Desktop, SDK, schema, package or operation recovery changes.
- One validation owner: existing CoreApiAdmissionMiddleware calls NativeAuthenticationRegistration for bearer signature and SQL active-session validation. Cookie endpoints retain anonymous admission, health with a bearer requires an active session, identity is assigned once. No parallel ASP.NET authentication scheme or challenge; transport/Origin/CSRF/body cap/readiness stay in existing middleware. Sanitized handled error logging uses only correlation ID; staged headers are cleared by existing boundary.
- Existing 293 frozen differential cases are unchanged. New 42 focused Kestrel cases cover strict credentials, body and query grammar, duplicate headers, cookie rejection, capability shape, optional bearer/revocation on health, failure header cleanup and exact cookie attributes. Existing CoreApiBoundaryTests cover degraded health and staged cookie/location cleanup. The existing disposable real HTTPS→SQL lifecycle fact is expanded with invalid bearer health, duplicate cookie rejection and revoked-session health; existing SQL gate still requires its original 16/16, with additional WEB6 protected-audit readiness cases retained.
- Two additional deterministic direct-controller post-commit cancellation cases assert store outcome, audit call and uncancelled token for refresh/revoke. The Kestrel optional-bearer cases count exactly one session-store validation for a valid bearer. Real disconnect/SQL post-commit fault evidence is not claimed by the direct oracle; independent review remains required. Attended Desktop UI path is unchanged; Windows net48 regression gates remain required.

## Gate ledger

| Gate | Required | Result at implementation |
| --- | --- | --- |
| G1 Linux SDK 10.0.404 locked build, Application/Host tests, actual inventory | 57 logical; 50 native / 7 bridge; all existing cases and 44/44 focused | NOT RUN locally |
| G2 frozen differential and native OpenAPI/contract tests | 293/293 unchanged, native metadata and login schema | NOT RUN locally |
| G3 Windows Desktop net48 regression | existing discovery, session and artifact gates | NOT RUN locally |
| G4 disposable verified HTTPS/SQL13 | existing lifecycle/SQL 16/16 plus all existing restricted-principal, restart and audit gates | NOT RUN locally |
| Independent review and exact final HEAD CI | all mandatory workflows success, no required skips; actual counts and run links in PR | PENDING |

Implementation environment: GitHub connector source inspection and atomic commit only; local checkout, SDK and SQL not available. No runtime PASS or independent human review claimed. CI receipts must be tied to final reviewed SHA. Protected staging/production VPS-08/09 and release acceptance remain NOT VERIFIED.
