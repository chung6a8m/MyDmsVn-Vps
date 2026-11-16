# Test and evidence policy — MyDmsVn-Vps

This is the **independent** policy of MyDmsVn-Vps. Archived/source-repository test policy, accepted exceptions, PASS counts and production releases are not binding. Current primary test assemblies are `net48` with xUnit v2; xUnit v3 migration is VPS-01.

## Non-negotiable evidence rule

A gate is PASS only after running it in the correct environment on an identified commit and inspecting actual execution. Record command, SHA, runner OS, SDK/Framework, test discovery, executed/failed/skipped counts, SQL instance class if relevant (not credentials), output/CI URL and failure disposition. Empty test selection, skipped-required tests, build-only, statics-only or a green prior commit is **not** a substitute.

## VPS-00B scope

| Gate | Environment | Requirement |
| --- | --- | --- |
| Source tree / project references | Linux Python 3 stdlib, Git checkout | Verify tracked local projects, solution refs, 13 embedded SQL scripts, no gitlinks/submodules or dynamic/escaping ProjectReferences |
| Desktop compile | Windows 2022 + .NET Framework 4.8 targeting pack + .NET SDK 8.0.400 | Locked restore + Release net48 build of Desktop.App |
| Legacy HttpHost compile | Same Windows runner | Locked restore + Release net48 build of Server.HttpHost |
| Legacy DbMigrator compile | Same Windows runner | Locked restore + Release net48 build of Server.DbMigrator |
| SQL-free smoke | Windows runner | Focused existing assemblies (Application, Architecture, Desktop.Session, HttpHost), non-empty TRX executed count |
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
