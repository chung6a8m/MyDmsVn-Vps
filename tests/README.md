# Tests — MyDmsVn-Vps independent baseline

This README replaces active operational instructions imported from the previous source repository. Its historical tests and fixtures remain in source form, but source-repo plans, accepted exceptions and evidence do **not** apply here.

See [test policy](../docs/testing/test-policy.md), [acceptance matrix](../docs/testing/acceptance-matrix.md), [build/CI commands](../docs/build/build-and-ci.md) and [VPS-00B evidence](../docs/evidence/vps-00b-baseline.md).

## Current targets

VPS-01 migrated 12 net48 xUnit test suites to `xunit.v3 3.2.2` as executable projects; VPS-03 adds a thirteenth opt-in acceptance suite. The separate CrashHost remains a helper executable, not an xUnit suite. **Compilation or discovery is not proof of SQL, GUI, or VPS-03 acceptance.** SQL/UI/acceptance owner suites are compiled and discovered only in unattended CI. Windows has the .NET Framework 4.8 runtime and, for UI tests, desktop/STA capabilities; Linux does not run the legacy assemblies.

SQL-free baseline smoke candidates: `MyDmsVn.Server.Application.Tests`, `MyDmsVn.Architecture.Tests`, `MyDmsVn.Desktop.Session.Tests` and `MyDmsVn.Server.HttpHost.Tests`. Inspect their TRX test counts: zero executed is not PASS. The SQL owners include `Infrastructure.Catalog`, `Foundation`, `Inventory`, `Security`, `Migrations` and shared `Infrastructure.IntegrationTests`; not all of their cases are SQL-free.

For SQL tests use only an explicitly configured, non-production `MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING` (master initial catalog), with authorized create/drop and appropriate privileges. Missing connection makes SQL-dependent cases NOT RUN even if xUnit marks them skipped; it is not a valid mandatory SQL acceptance result.

Canonical WEB2–WEB6 request/response test fixtures are local in [contracts corpora](../docs/contracts/README.md); they are test data, not inherited operational instructions.

GUI/native/125%-scale scenarios require attended Windows evidence. Headless GitHub Actions compile/tests cannot substitute. Do not run destructive SQL integration against a production instance. See the test policy for focused examples and detailed rules.

## VPS-01 commands and nonempty gates

`python3 scripts/verify-vps-01.py` checks package/project topology and pinned HTTP route metadata on Linux (static only).

On Windows 2022, run each of the four SQL-free suites with `dotnet test <csproj> -c Release -f net48 --no-restore --logger "trx;LogFileName=<id>.trx"`; independently inspect the TRX total/executed/passed/not-executed. Their minimum actual execution counts are 13 (Architecture), 212 (Desktop.Session), 363 (Application) and 640 (HttpHost). Nine additional suites, including `MyDmsVn.Vps03.Acceptance.Tests`, use `dotnet test <csproj> -c Release -f net48 --no-build --no-restore --list-tests` to prove that the xUnit v3 runner discovers cases **without running them**. Missing test output or fewer than expected selected cases is not PASS; discovery of the VPS-03 suite is specifically not Issue #8 execution evidence.

See [VPS-01 evidence](../docs/evidence/vps-01-foundation.md). The SQL env/attended GUI gates remain independently NOT RUN without their appropriate environments. `tests/Directory.Build.targets` lists narrowly scoped legacy analyzer debts `xUnit1051` and `xUnit3003`; it does not suppress build, test, runtime or product warnings.
