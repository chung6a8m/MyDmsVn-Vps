# VPS-02 — HTTP API parity acceptance evidence

**Date:** 2026-11-16. **Repository:** chung6a8m/MyDmsVn-Vps.
**Implementation PR:** [#3](https://github.com/chung6a8m/MyDmsVn-Vps/pull/3).
**SQL acceptance PR:** [#5](https://github.com/chung6a8m/MyDmsVn-Vps/pull/5).
**Acceptance base SHA:** `8f1fa89d519afd1ba11ce43adf4ecd4a3545e85d`.
**SQL acceptance implementation SHA:** `91bab7d8966629e11e5de242832098ca6f2dc2bb`.

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
| xUnit v3 HttpHost hosted tests | PASS — 683/683, zero skipped | `vps02-httphost-91bab7d.trx` |
| 29 frozen routes, 178 WEB fixtures and 28 new signatures | PASS locally | Static verifier plus 683-test HttpHost regression |
| Error/JSON/permission/concurrency boundaries | PASS — 7/7 SQL HTTP cases | `vps02-http-sql-91bab7d-final.trx` |
| Exact SQL principal admitted/rejected | PASS — 7/7, zero skipped | `vps02-principal-91bab7d.trx` |
| Catalog and Security SQL writes/rollback/revision | PASS on exact11/12/13 | Restricted OWIN SQL matrix and packaged HTTPS host |
| Packaged net48 HttpHost over real HTTPS | PASS — enabled plus disabled, exact11/12/13 | `vps02-iis-https-91bab7d.trx` |
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
  3. Each final green fixture created and verified cleanup of its uniquely owned
  nonproduction database/login.
- IIS Express used the existing `localhost:44321` HTTPS binding and certificate. The
  test changed no machine TLS/IIS configuration and pinned the bound certificate for
  the owned child process.

### Commands and observed counts

The accepted selections below ran from repository root at
`91bab7d8966629e11e5de242832098ca6f2dc2bb`. The SQL connection environment variable
was supplied by the operator before these commands and was never echoed.

```powershell
dotnet restore tests/MyDmsVn.Server.Infrastructure.Security.Tests/MyDmsVn.Server.Infrastructure.Security.Tests.csproj --locked-mode
dotnet test tests/MyDmsVn.Server.Infrastructure.Security.Tests/MyDmsVn.Server.Infrastructure.Security.Tests.csproj -c Release -f net48 --no-restore --filter 'FullyQualifiedName~Vps02DesktopApiPrincipalSqlTests' --logger 'trx;LogFileName=vps02-principal-91bab7d.trx' --results-directory 'TestResults/vps-02-final-91bab7d'
dotnet test tests/MyDmsVn.Server.Infrastructure.Security.Tests/MyDmsVn.Server.Infrastructure.Security.Tests.csproj -c Release -f net48 --no-restore --no-build --filter 'FullyQualifiedName~Vps02DesktopApiHttpSqlTests' --logger 'trx;LogFileName=vps02-http-sql-91bab7d-final.trx' --results-directory 'TestResults/vps-02-final-91bab7d'
./src/MyDmsVn.Server.HttpHost/Publish-Web2Host.ps1 -OutputPath 'TestResults/vps02-published-91bab7d'
$binding = netsh http show sslcert ipport=0.0.0.0:44321 | Out-String
$match = [regex]::Match($binding, 'Certificate Hash\s*:\s*([0-9a-fA-F]+)')
if (-not $match.Success) { throw 'HTTPS certificate binding not found for port 44321.' }
$env:MYDMSVN_WEB_TEST_TLS_THUMBPRINT = $match.Groups[1].Value
$env:MYDMSVN_WEB_TEST_PUBLISH_DIRECTORY = (Resolve-Path 'TestResults/vps02-published-91bab7d').Path
dotnet test tests/MyDmsVn.Server.Infrastructure.Security.Tests/MyDmsVn.Server.Infrastructure.Security.Tests.csproj -c Release -f net48 --no-restore --no-build --filter 'FullyQualifiedName~Published_SystemWeb_desktop_API|FullyQualifiedName~Published_SystemWeb_disabled_desktop_API' --logger 'trx;LogFileName=vps02-iis-https-91bab7d.trx' --results-directory 'TestResults/vps-02-final-91bab7d'
dotnet restore tests/MyDmsVn.Server.HttpHost.Tests/MyDmsVn.Server.HttpHost.Tests.csproj --locked-mode
dotnet test tests/MyDmsVn.Server.HttpHost.Tests/MyDmsVn.Server.HttpHost.Tests.csproj -c Release -f net48 --no-restore --logger 'trx;LogFileName=vps02-httphost-91bab7d.trx' --results-directory 'TestResults/vps-02-final-91bab7d'
python scripts/verify-vps-01.py
```

| Selection | Discovered | Executed | Passed | Failed | Skipped / not executed |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Vps02DesktopApiPrincipalSqlTests` | 7 | 7 | 7 | 0 | 0 |
| `Vps02DesktopApiHttpSqlTests` (exact11/12/13 plus disabled mode) | 7 | 7 | 7 | 0 | 0 |
| Packaged IIS Express HTTPS enabled exact11/12/13 plus disabled mode | 2 | 2 | 2 | 0 | 0 |
| Full `MyDmsVn.Server.HttpHost.Tests` regression | 683 | 683 | 683 | 0 | 0 |

The SQL HTTP matrix exercises all four Catalog resources through create/update,
get/list/lookup/active; allowed and forbidden principals; strict invalid-body
rejection; Security permission check/catalog, users, roles, mutation, import
preview/apply; stale revision; concurrent apply; audit failure rollback; and audit
persistence. It runs the production OWIN pipeline with every runtime connection
impersonating the provisioned restricted Web principal.

The packaged-host case repeats all four Catalog resources with value, filter, paging,
lookup, update, inactive and SQL-row oracles; Security permission/catalog/users/roles,
denial, stale/concurrent import, audit and physical rollback checks; and error
non-disclosure through the published net48 System.Web host on real HTTPS for exact11,
exact12 and exact13. Its separate disabled-mode case returns 404 for both new Catalog
and Security routes while the original SQL-backed Catalog lookup remains 200.

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
   WEB4/WEB5 gates remain unchanged.
4. Independent review found shallow SQL/HTTPS assertions and an admission gap for
   schema-scoped `EXECUTE`. The principal validator now rejects schema DML/EXECUTE,
   role-revocation admission is covered, Catalog values/filter/paging/concurrency and
   Security deny/audit/rollback are checked at SQL level, and packaged disabled mode
   is executed. The committed reruns passed 7/7 principal, 7/7 OWIN SQL, 2/2 packaged
   HTTPS and 683/683 HttpHost with zero skipped.

One attempted parallel invocation at the accepted SHA was invalidated by an MSBuild
file lock (`MSB3027`) because two commands targeted the same test executable. It is
not counted above; the affected HTTP matrix was rerun sequentially with `--no-build`
and passed 7/7. Deliberately failing red/review fixtures retain their uniquely owned
SQL/run artifacts for manual review under the repository safety policy; they were not
silently deleted or counted as acceptance. Final green fixtures verified owned SQL
cleanup before PASS.

## Prior implementation review

PR #3 independently reviewed all 22 implementation files and fixed two findings
before merge: the Desktop grant now requires prior membership in
`MyDmsVnWebRuntimeRole` (SQL 51113), and the SQL allowlist expansion now refuses
missing or duplicate source markers. Issue #4 owns the separate real-SQL closeout
recorded above.
