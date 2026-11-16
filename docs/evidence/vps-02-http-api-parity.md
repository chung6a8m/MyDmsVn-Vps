# VPS-02 — HTTP API parity evidence (working, not accepted)

**Date:** 2026-11-16. **Repository:** chung6a8m/MyDmsVn-Vps.
**PR:** [draft #3](https://github.com/chung6a8m/MyDmsVn-Vps/pull/3).
**Base SHA:** 767f352183bed6c66af5c677977232c9907bd23d.
Use the PR to determine final head SHA; this file must not claim that an older CI run
passes after a review-fix commit.

## Source-level implementation

- VPS-02A: Catalog four resources × five CRUD/list/get/active routes = 20.
- VPS-02B: permission check/catalog, Security users/roles, mutation, import
  preview/apply = 7; Foundation status = 1.
- Source inventory: 29 preserved + 28 added = 57 routes; 46/46 Desktop API
  interface methods assigned to a route (documented JSON matrices).
- New routes are opt-in, require a valid Bearer SQL-backed session and reuse
  existing Application authorization, validation, business errors and audit.
- SQL role grants only four Catalog INSERT/UPDATE and three Security SP EXECUTE;
  no direct Security DML. New SQL role/config mismatches fail at startup.

## Test gates — never infer PASS

| Gate | State | Evidence needed |
| --- | --- | --- |
| Locked restore/net48 Release build, Windows 2022 | PENDING | PR #3 final-head Actions logs |
| xUnit v3 HttpHost OWIN hosted tests | PENDING | Windows TRX counts, no skip/failure |
| 29 frozen routes, 178 WEB fixtures and 28 new route signatures | PENDING | Reflection and Linux topology jobs |
| New error/JSON/permission/concurrency boundary tests | PENDING | Hosted OWIN tests on final HEAD |
| Exact SQL principal admitted/rejected, schema11/12/13 | NOT RUN | Operator-approved isolated SQL instance |
| Catalog CRUD and Security Administration SQL writes/rollback/revision | NOT RUN | Restricted runtime-user SQL oracle |
| Desktop net48 -> HTTP -> SQL E2E | NOT RUN (VPS-03) | Live Windows client |
| Linux ASP.NET Core net10 | NOT RUN (VPS-04) | Linux runtime deployment |

**No accepted exception.** VPS-02 cannot be COMPLETE until applicable acceptance
gates have executed successfully and final-head evidence is recorded. PR remains draft.

Review finding (PR #3 first CI): 14 legacy Catalog lookup tests failed due to ambiguous /{id} matching /lookup. The new {id:int} route constraint fixes the declaration conflict without altering the 29 legacy signatures. CI must rerun on the new HEAD; the first failed run is not passing evidence. The three ForSession methods are local-only; remote mapping remains 46.
