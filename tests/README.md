# Tests — MyDmsVn-Vps independent baseline

This README replaces active operational instructions imported from the previous source repository. Its historical tests and fixtures remain in source form, but source-repo plans, accepted exceptions and evidence do **not** apply here.

See [test policy](../docs/testing/test-policy.md), [acceptance matrix](../docs/testing/acceptance-matrix.md), [build/CI commands](../docs/build/build-and-ci.md) and [VPS-00B evidence](../docs/evidence/vps-00b-baseline.md).

## Current targets

Main test suites under `tests/` are `net48` and currently use xUnit v2. VPS-01 will migrate to xunit.v3 with verified discovery. Windows has the .NET Framework 4.8 runtime and, for UI tests, desktop/STA capabilities; Linux does not run the legacy assemblies.

SQL-free baseline smoke candidates: `MyDmsVn.Server.Application.Tests`, `MyDmsVn.Architecture.Tests`, `MyDmsVn.Desktop.Session.Tests` and `MyDmsVn.Server.HttpHost.Tests`. Inspect their TRX test counts: zero executed is not PASS. The SQL owners include `Infrastructure.Catalog`, `Foundation`, `Inventory`, `Security`, `Migrations` and shared `Infrastructure.IntegrationTests`; not all of their cases are SQL-free.

For SQL tests use only an explicitly configured, non-production `MYDMSVN_TEST_SQLSERVER_CONNECTION_STRING` (master initial catalog), with authorized create/drop and appropriate privileges. Missing connection makes SQL-dependent cases NOT RUN even if xUnit marks them skipped; it is not a valid mandatory SQL acceptance result.

Canonical WEB2–WEB6 request/response test fixtures are local in [contracts corpora](../docs/contracts/README.md); they are test data, not inherited operational instructions.

GUI/native/125%-scale scenarios require attended Windows evidence. Headless GitHub Actions compile/tests cannot substitute. Do not run destructive SQL integration against a production instance. See the test policy for focused examples and detailed rules.
