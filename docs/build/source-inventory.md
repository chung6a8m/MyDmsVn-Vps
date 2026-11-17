# Source/build dependency inventory — VPS-00B

Measured at imported `main` commit `086e8699797b05600ad5d7b256b4175e67e4c8c6`. These counts reflect the *files*, not successful compilation.

| Tree group | C# projects | Notes |
| --- | ---: | --- |
| `src/` | 11 | Contracts, 4 Desktop projects, 5 Server projects, SharedKernel; all `net48` |
| `tests/` | 13 | 12 primary tests + WebOperation.CrashHost supporting executable |
| `samples/` | 4 | UI labs, legacy WinForms |
| `tools/` | 3 | Legacy host/operation tools |
| `vendor/MyDmsVn.WinFormsKit/` | 13 | Source snapshot; 2 samples, 2 library src, 3 tests and 6 nested SourceGrid projects |
| **Total** | **44** | 0 gitlinks, 0 missing relative ProjectReference from completed VPS-00A source check |

The *primary* `src/` and test projects still target net48. Some vendored/sample projects may have their own targets; they are not Linux Server deliverables. `src/MyDmsVn.Server.DbMigrator/Scripts/` has the 13 immutable embedded SQL scripts 001–013; [import checksums](../import/sql-checksums.sha256) are retained.

## Concrete current reference edges

- Desktop.App -> Desktop.Infrastructure.Local, Desktop.WinForms, **Server.Infrastructure**.
- Desktop.Infrastructure.Local -> Desktop.Application, **Server.Application**.
- Desktop.WinForms -> Desktop.Application, vendored BootstrapSourceGrid.
- Server.HttpHost -> Contracts, Server.Application, Server.Infrastructure.
- Server.DbMigrator -> Server.Infrastructure.
- Test assemblies (especially Infrastructure owners) reference Desktop Local/Server assemblies for legacy fixtures.

**Transitional debt, not VPS-00B defects:** Desktop to Server refs; net48 HttpHost; DbUp/MediatR/Newtonsoft; shared contract serializer coupling; mixed test dependencies. Governed by [target boundaries](../architecture/dependency-boundaries.md) and [roadmap](../roadmap/vps-00-vps-09.md).

## Test fixture recovery in VPS-00B

The first Windows HttpHost smoke revealed 211 failures due to absent canonical JSON corpora under `docs/contracts/` (these were excluded along with source governance during import). VPS-00B restores the **178 immutable contract fixture files only**, with an [independent register](../contracts/README.md). These assets are now local; no cross-repository fetch is needed at build or test time.

## Import isolation already verified

VPS-00A compared 2,282/2,282 file contents/modes from three pinned sources, recorded a new independent history, and removed submodules, AppCore and inherited active docs/workflows. The new static checker additionally verifies **current checkout** references and mandatory docs on every CI run. It does not attest to runtime behavior or re-compare upstream history.

## Current build inputs

Root `global.json` chooses SDK 8.0.400 (latestFeature roll-forward); `Directory.Packages.props` enables Central Package Management; packages.lock.json retained; `NuGet.config` lists nuget.org. HttpHost and its test project have legacy locally pinned PackageReference versions. Changes to these are deferred to focused upgrades; do not conflate them with CI foundation.

No sibling checkout of MyDmsVn, WinFormsKit or SourceGrid is required as an MSBuild project reference. External public NuGet feed usage is normal package restore, not cross-repository source coupling.

## VPS-03 incremental inventory

The VPS-00B table above is intentionally historical. VPS-03 adds the net48 Desktop.Infrastructure.Http production project and the net48 MyDmsVn.Vps03.Acceptance.Tests project (src/ now 12, tests/ now 14 and repository total 46). Production Desktop.App references Http + WinForms instead of Local + Server. The automated published-package audit and exact-SHA attended Windows GUI/package acceptance passed under Issue #8 at `4c0b1be`; final PR review/CI and Issue #7 remain separate.
