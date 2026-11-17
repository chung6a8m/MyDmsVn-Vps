# VPS-03 Issue #7 closeout evidence

**Disposition:** local Issue #7 acceptance PASS; this is not production
approval and does not make VPS-03 complete. Base SHA:
`f266c68958c7d3b512e1f5e036122ab6341bed5e`. Exact package and acceptance
HEAD: `40b7ebc00d703a169566b27004825f376418ab8c`, 17 November 2026. PR #6
remains Draft pending final-HEAD CI, independent review, and merge.

## Environment and package

- Attended Windows 11 session, .NET Framework 4.8, .NET SDK 8.0.428 using the
  repository's 8.0.400 feature-band policy, MSBuild 17.11.71, and IIS Express
  10.0.26013.1000.
- Operator-designated non-production SQL Server with create/drop privileges;
  the connection value was supplied only through the approved environment
  variable and is not recorded. Canonical HTTPS authority was localhost with a
  pre-trusted certificate thumbprint; no certificate bypass was enabled.
- Desktop Release net48 x86 SHA-256:
  `6ea4fd47ecf8b55143d75a64b4b074a72fc205c949740d6c8b3b29f6e52997a7`.
  Host SHA-256:
  `b8720d3b1eaa454b20e25e695ccd76b9b05d2960ddc9e5c150305ad602efab0b`.
- Publish audit: PE machine `0x014c`, 23 lock entries, 36 published files,
  zero forbidden Local/Server/SQL/MediatR/DbMigrator binaries or secret/SQL
  configuration markers.

## Exact Issue #7 acceptance

The strict runner wrote results beneath
`TestResults/vps03-issue7/40b7ebc00d703a169566b27004825f376418ab8c`.
The exact commands were:

```powershell
pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -PreflightOnly
pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -LiveOnly
pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -AttendedOnly
pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 `
  -LiveResult TestResults/vps03-issue7/40b7ebc00d703a169566b27004825f376418ab8c/results/live-result.json `
  -AttendedResult TestResults/vps03-issue7/40b7ebc00d703a169566b27004825f376418ab8c/results/attended/attended-result.json
```

| Group | Discovered/executed/passed | Failed | Skipped/not executed |
| --- | ---: | ---: | ---: |
| Crash before/after durable save | 2/2/2 | 0 | 0 |
| Corruption, ACL, origin binding, exact clear | 4/4/4 | 0 | 0 |
| Live restart/actor/grant/session/terminal/operator/unjournalled | 7/7/7 | 0 | 0 |
| Attended Native | 1/1/1 | 0 | 0 |
| **Total** | **14/14/14** | **0** | **0** |

The attended run accepted all nine checkpoints: LOGIN, SHELL, CATALOG, GOODS,
SALES, SECURITY, ERRORS, LOGOUT, and UNKNOWN-RECOVERY. The recovery surface
showed the immutable operation identity and no-retry warning, fenced the shell,
enabled only the read-only status check, returned the exact authenticated WEB5
`NOT_EXECUTED` terminal, required a separate confirmation and fresh reread, and
finished with `No pending write` plus disabled recovery actions. Desktop exited
cleanly. Owned child processes, IIS process, SQL database/login, and test-owned
pending intent were absent after teardown. No required case was skipped.

## Regression and discovery floors

After cleaning mixed x86 build outputs, the four SQL-free suites ran
sequentially on the same code HEAD:

| Executed suite | Passed/total | Floor | Failed/skipped |
| --- | ---: | ---: | ---: |
| Architecture | 15/15 | 13 | 0/0 |
| Desktop.Session | 238/238 | 212 | 0/0 |
| Server.Application | 363/363 | 363 | 0/0 |
| HttpHost | 683/683 | 640 | 0/0 |
| **Total** | **1,299/1,299** | 1,228 | **0/0** |

Discovery-only (not execution) met every committed floor: Desktop 813/813,
Desktop.Ui 382/382, SQL Catalog 19/17, Foundation 66/66, Harness 59/59,
Inventory 208/127, Migrations 175/107, Security 1,756/1,684, and VPS-03
acceptance 58/38.

An initial Architecture attempt after the x86 acceptance build failed 7 tests
with `BadImageFormatException` because shared Release outputs had mixed machine
architecture. It is not counted as passing evidence. A solution clean followed
by isolated sequential builds produced the authoritative 1,299/1,299 result.

## Remaining boundaries

Catalog and Security do not have WEB4/WEB5 journals. Their authorized readback
is advisory and cannot establish exact commit or rollback, so an ambiguous
unjournalled write can remain permanently UNKNOWN. There is no local force
clear and no general operator HTTP release endpoint. Closing Issue #7 accepts
that fail-closed limitation; it does not approve production deployment, merge
PR #6, or complete VPS-03/VPS-09. Final documentation-HEAD CI URLs and job
counts are recorded in PR #6 after the status commit to avoid self-reference.
