# VPS-02 — HTTP API parity acceptance evidence

**Date:** 2026-11-16. **Repository:** chung6a8m/MyDmsVn-Vps.
**Implementation PR:** [#3](https://github.com/chung6a8m/MyDmsVn-Vps/pull/3).
**SQL acceptance PR:** [#5](https://github.com/chung6a8m/MyDmsVn-Vps/pull/5).
**Acceptance base SHA:** `8f1fa89d519afd1ba11ce43adf4ecd4a3545e85d`.
**SQL test implementation SHA:** `53fe6fad8b9b87078f764d544d2f4eecc52aa800`.

The final PR head SHA and CI run URL belong to PR #5 after all review fixes; an
older local or CI run does not pass a later head.

## Accepted implementation scope

- VPS-02A: Catalog four resources × five CRUD/list/get/active routes = 20.
- VPS-02B: permission check/catalog, Security users/roles, mutation, import
  preview/apply = 7; Foundation status = 1.
- Source inventory: 29 preserved + 28 added = 57 routes; all 46 remote Desktop API
  methods have an assigned route.
- New routes remain opt-in, require a valid Bearer SQL-backed session and reuse
  existing Application authorization, validation, business errors, transactions and
  audit.
- The opt-in SQL role grants only four Catalog INSERT/UPDATE pairs and three Security
  stored-procedure EXECUTEs. It has no direct Security DML. Role/config mismatch
  refuses startup.

## Gate result

| Gate | State | Evidence |
| --- | --- | --- |
| Locked restore/net48 Release build, Windows | PASS locally; final-head CI pending | Commands and environment below; PR #5 final-head Actions |
| xUnit v3 HttpHost hosted tests | PASS — 683/683, zero skipped | `vps02-httphost-regression.trx` |
| 29 frozen routes, 178 WEB fixtures and 28 new signatures | PASS locally | Static verifier plus 683-test HttpHost regression |
| Error/JSON/permission/concurrency boundaries | PASS — 7/7 SQL HTTP cases | `vps02-http-sql-matrix-green.trx` |
| Exact SQL principal admitted/rejected | PASS — 4/4, zero skipped | `vps02-principal-after-fix.trx` |
| Catalog and Security SQL writes/rollback/revision | PASS on exact11/12/13 | Restricted OWIN SQL matrix and packaged HTTPS host |
| Packaged net48 HttpHost over real HTTPS | PASS on exact11/12/13 | `vps02-iis-https-green2.trx` |
| Desktop net48 -> HTTP -> SQL E2E | NOT RUN (VPS-03) | Live Windows Desktop cutover gate |
| Linux ASP.NET Core net10 | NOT RUN (VPS-04) | Linux runtime gate |

**No accepted exception.** VPS-02 becomes COMPLETE when PR #5 is independently
reviewed, final-head CI passes and the PR merges. The local SQL and HTTPS gates below
contain no skipped, failed or empty selection.

## SQL-backed acceptance execution

### Environment and safety

- Windows `Microsoft Windows NT 10.0.26300.0`; .NET SDK `8.0.428` selected by
  repository `global.json` (`8.0.400`, `latestFeature`); net48 VSTest 17.11.2 x64.
- Operator-provided `MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING` was present with
  `Initial Catalog=master` and Windows integrated authentication. No connection
  string, server name, token, password or certificate thumbprint was logged.
- SQL Server Developer Edition 64-bit, product version `14.0.2150.2`, engine edition
  3. Every test created and dropped a uniquely owned nonproduction database.
- IIS Express used the existing `localhost:44321` HTTPS binding and certificate. The
  test changed no machine TLS/IIS configuration and pinned the bound certificate for
  the owned child process.

### Commands and observed counts

The SQL selections ran sequentially from repository root.

```powershell
dotnet restore tests/MyDmsVn.Server.Infrastructure.Security.Tests/MyDmsVn.Server.Infrastructure.Security.Tests.csproj --locked-mode
dotnet test tests/MyDmsVn.Server.Infrastructure.Security.Tests/MyDmsVn.Server.Infrastructure.Security.Tests.csproj -c Release -f net48 --no-restore --filter 'FullyQualifiedName~Vps02DesktopApiPrincipalSqlTests' --logger 'trx;LogFileName=vps02-principal-after-fix.trx'
dotnet test tests/MyDmsVn.Server.Infrastructure.Security.Tests/MyDmsVn.Server.Infrastructure.Security.Tests.csproj -c Release -f net48 --no-restore --filter 'FullyQualifiedName~Vps02DesktopApiHttpSqlTests' --logger 'trx;LogFileName=vps02-http-sql-matrix-green.trx'
./src/MyDmsVn.Server.HttpHost/Publish-Web2Host.ps1 -OutputPath <owned-empty-test-directory>
dotnet test tests/MyDmsVn.Server.Infrastructure.Security.Tests/MyDmsVn.Server.Infrastructure.Security.Tests.csproj -c Release -f net48 --no-restore --filter 'FullyQualifiedName~Published_SystemWeb_desktop_API' --logger 'trx;LogFileName=vps02-iis-https-green2.trx'
dotnet restore tests/MyDmsVn.Server.HttpHost.Tests/MyDmsVn.Server.HttpHost.Tests.csproj --locked-mode
dotnet test tests/MyDmsVn.Server.HttpHost.Tests/MyDmsVn.Server.HttpHost.Tests.csproj -c Release -f net48 --no-restore --logger 'trx;LogFileName=vps02-httphost-regression.trx'
python scripts/verify-vps-01.py
```

| Selection | Discovered | Executed | Passed | Failed | Skipped / not executed |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Vps02DesktopApiPrincipalSqlTests` | 4 | 4 | 4 | 0 | 0 |
| `Vps02DesktopApiHttpSqlTests` (exact11/12/13 plus disabled mode) | 7 | 7 | 7 | 0 | 0 |
| Packaged IIS Express HTTPS exact11/12/13 | 1 | 1 | 1 | 0 | 0 |
| Full `MyDmsVn.Server.HttpHost.Tests` regression | 683 | 683 | 683 | 0 | 0 |

The SQL HTTP matrix exercises all four Catalog resources through create/update,
get/list/lookup/active; allowed and forbidden principals; strict invalid-body
rejection; Security permission check/catalog, users, roles, mutation, import
preview/apply; stale revision; concurrent apply; audit failure rollback; and audit
persistence. It runs the production OWIN pipeline with every runtime connection
impersonating the provisioned restricted Web principal.

The packaged-host case repeats the critical Catalog/Security, denial,
stale/concurrent and SQL-oracle checks through the published net48 System.Web host on
real HTTPS for exact11, exact12 and exact13. Disabled mode returns 404 for new routes
while the original SQL-backed Catalog lookup remains 200.

`scripts/verify-vps-01.py` reported 12 executable xUnit v3 projects, the original 29
routes, all 178 frozen fixtures, all 28 additive routes and all 46 Desktop method
mappings. The full HttpHost run independently executed 683 tests with zero skip.

### Findings fixed before acceptance

1. The first real principal run executed 4 tests: 3 passed and 1 failed because the
   new oracle used nonexistent `Products.Id`. The production schema uses
   `ProductId`; the corrected oracle then passed 4/4.
2. The first seven-case SQL HTTP matrix executed all 7: 4 passed and 3 failed because
   the audit-trigger rollback oracle expected HTTP 409. The pipeline correctly hides
   the injected SQL exception as 500; the final assertion checks 500, no SQL-message
   disclosure and physical rollback. The rerun passed 7/7.
3. Initial packaged-host attempts exposed that unrelated legacy Sales smoke lacks
   WEB4/WEB5 idempotency/epoch headers. VPS-02 exact12/13 omit only that unrelated
   legacy mutation while retaining auth, TLS, Desktop API and SQL oracles; dedicated
   WEB4/WEB5 gates remain unchanged. The final exact11/12/13 HTTPS run passed 1/1.

## Prior implementation review

PR #3 independently reviewed all 22 implementation files and fixed two findings
before merge: the Desktop grant now requires prior membership in
`MyDmsVnWebRuntimeRole` (SQL 51113), and the SQL allowlist expansion now refuses
missing or duplicate source markers. Issue #4 owns the separate real-SQL closeout
recorded above.
