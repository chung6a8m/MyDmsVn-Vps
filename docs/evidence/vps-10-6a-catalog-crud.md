# VPS-10.6A — Native Catalog CRUD

- Base: merged PR #40, main `9f80cbf9203d7e83a8fe61f79483ec93ce509bc2`.
- Source scope: Products, Warehouses, Employees, Customers, five existing routes each. Expected 36 MVC native / 21 bridge; all 57 original verb/path/action identities, 293 independently frozen differential cases and 46 Desktop remote methods retained.
- Native typed query and body DTOs, ID-before-query-before-body for mutations; detail query/body/id; collection body/query. Strict JSON field allowlists, duplicate/unknown rejection and optional-null compatibility. Existing response contracts, Application permission/validation/transactions and Desktop UNKNOWN policy unchanged.
- Catalog feature metadata preserves disabled-mode 404 after transport/auth admission and before binding. Existing lookups are not gated.
- 69 additional focused HTTP cases required, including per-route permission checks, disabled/malformed precedence, canonical integers, bool and Unicode. Existing independent differential cases remain unmodified.
- G1/G2/G3/G4: NOT VERIFIED in editing session. The protected Linux/Windows and disposable HTTPS/SQL gates must be checked against exact final PR HEAD. No local full checkout/.NET SDK/SQL Server/Windows GUI available. Source inspection is not runtime evidence. Catalog-specific real SQL mutation and race coverage is still required before completion; no release approval, no automatic retry, no change to VPS-08/09.
