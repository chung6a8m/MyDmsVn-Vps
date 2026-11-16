# Test and evidence policy — MyDmsVn-Vps

This is the **independent** policy of MyDmsVn-Vps. Archived/source-repository test policy, accepted exceptions, PASS counts and production releases are not binding. Current primary test assemblies are `net48` with xUnit v2; xUnit v3 migration is VPS-01.

## Non-negotiable evidence rule

A gate is PASS only after running it in the correct environment on an identified commit and inspecting actual execution. Record command, SHA, runner OS, SDK/Framework, test discovery, executed/failed/skipped counts, SQL instance class if relevant (not credentials), output/CI URL and failure disposition. Empty test selection, skipped-required tests, build-only, statics-only or a green prior commit is **not** a substitute.

## VPS-00B scope

| Gate | Environment | Requirement |
| --- | --- | --- |
| Source tree / project references | Linux Python 3 stdlib, Git checkout | Verify tracked local projects, solution refs, 13 embedded SQL scripts, 178 frozen corpus Git blobs, no gitlinks/submodules or escaping literal build paths |
| Desktop compile | Windows 2022 + .NET Framework 4.8 targeting pack + .NET SDK 8.0.400 | Locked restore + Release net48 build of Desktop.App |
| Legacy HttpHost compile | Same Windows runner | Locked restore + Release net48 build of Server.HttpHost |
| Legacy DbMigrator compile | Same Windows runner | Locked restore + Release net48 build of Server.DbMigrator |
| SQL-free smoke | Windows runner | Focused existing assemblies (Application, Architecture, Desktop.Session, HttpHost); non-empty TRX and no failed/skipped/not-executed selected tests |
| SQL integration | Explicit isolated SQL Server | Deferred to separately provisioned gate, **NOT RUN** if environment absent |
| GUI/native 100%/125% | Attended Windows session | Deferred; headless runner success cannot substitute |
| ASP.NET Core 10 on Linux | Actual Linux runtime | Not applicable until VPS-04; static check is not a Linux Server build |

## Commands and isolation

Use `dotnet restore <project> --locked-mode` before building or testing with `--no-restore`; never update package locks silently in CI. Test example:

```powershell
$test = 'tests/MyDmsVn.Server.Application.Tests/MyDmsVn.Server.Application.Tests.csproj'
dotnet restore $test --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }
dotnet test $test -c Release -f net48 --no-restore --logger 'trx;LogFileName=application.trx' --results-directory TestResults
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
```

Review the resulting TRX `Counters.total` and `Counters.executed`; both must be positive before calling a selected test run PASS. Results with skipped required cases are not complete evidence.

## SQL safety

The only approved test connection variable in the imported harness is `MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING`. Tests may create/drop uniquely owned databases on an **operator-designated non-production** SQL Server. Use a reviewed `master` initial catalog and required privileges. Never point tests at production; never default to localdb, print secrets, or invent a SQL URL. If absent, `SqlServerFact` may skip: record **NOT RUN**, not PASS. Execute SQL integration sequentially where tests share instance resources.

## Frozen fixture corpus

The first CI run exposed missing imported WEB2–WEB6 fixture assets. VPS-00B recovers them as **local, immutable** technical files under `docs/contracts/`, with an independent provenance register. Existing HttpHost tests read those paths; never fetch them from upstream at test time. Preserve their exact input/output JSON until a separately accepted contract migration.

## Contract, concurrency and E2E later

VPS-01 freezes HTTP JSON fixtures; VPS-02/03 compare endpoint and Desktop behavior. VPS-05 requires serializer/fingerprint parity; VPS-06 requires permission/pipeline/transaction parity; VPS-07 uses real SQL migration oracle with interrupted/concurrent/unknown-state cases; VPS-09 requires complete Desktop+Linux+SQL scenarios. Always rerun focused tests and final-head CI after changes. Exceptions need dated Owner approval with explicit scope; do not disguise a failure as skipped or a successful job.

## VPS-01 — xUnit v3 migration and discovery contract

All **12** net48 xUnit test projects build as executable test projects using `xunit.v3 3.2.2`, `xunit.runner.visualstudio 3.1.5` and `Microsoft.NET.Test.Sdk 17.14.1` (VSTest; not Microsoft.Testing.Platform). The remaining `MyDmsVn.WebOperation.CrashHost` is a helper executable, not an xUnit suite. Tests continue to use the pinned SDK **8.0.400**; SDK 10.0.404 is reserved for VPS-04.

The Windows smoke workflow executes **four** SQL-free suites and checks their actual TRX totals against the independent VPS-00B floors: Architecture >=13, Desktop.Session >=212, Server.Application >=363, HttpHost >=640. A missing TRX, empty selection, skipped smoke case or fewer discovered/executed cases **fails** the gate. The VPS-01 workflow builds and **discovers without running** eight other suites on Windows, including Desktop GUI and SQL owner suites. Discovery only is never reported as SQL/GUI test PASS. The Linux topology gate verifies all twelve projects and the route/fixture inventory; it does not execute net48 assemblies.

xUnit v3 custom SQL Fact/Theory category traits use `Xunit.v3.ITraitAttribute.GetTraits()`; the missing-SQL skip behavior is preserved. Test-only `tests/Directory.Build.targets` suppresses exactly two newly introduced analyzer diagnostics: `xUnit1051` (legacy tests intentionally choose cancellation tokens as the test oracle) and `xUnit3003` (custom legacy Fact/Theory subclasses omit optional source-file metadata). These are documented **static analyzer migration debts**, not waivers for failing tests or SQL integration. Production `src/` analyzers and warnings-as-errors remain active. Review these suppressions separately in future test modernization; do not broaden them.

New contract gates compare the 29 source-declared controller route signatures against `docs/contracts/vps-01-route-inventory.json`, verify 178 local frozen WEB2-WEB6 fixture names and pin representative Newtonsoft success/error envelopes including decimal/int64 lexical forms and HTTP 401. The 29-row inventory is a **source map**, not evidence of live endpoint parity. Route coverage, Desktop HTTP parity, permission and SQL-backed effects remain VPS-02/03 gates.

See [VPS-01 evidence](../evidence/vps-01-foundation.md) and [route inventory](../contracts/vps-01-route-inventory.json).

### Eight additional Windows discovery floors (VPS-01)

Run `dotnet test --list-tests` on each `net48` executable and count only indented `MyDmsVn.*` fully qualified test-case lines (not VSTest diagnostics). Windows-2022 VPS-01 reference run [#43850012712](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/43850012712) discovered: Desktop **813**, Desktop.Ui **382**, SQL Catalog **17**, SQL Foundation **66**, SQL Harness **59**, SQL Inventory **127**, SQL Migrations **107**, SQL Security **1,684** — **3,255** listed cases total. The CI matrix fails if any suite drops below its floor; intentional changes require a reviewed inventory update. **These cases are discovered-only, not executed tests.**
