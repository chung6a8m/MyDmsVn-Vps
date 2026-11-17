# VPS-03 Issue #7 closeout evidence

**Disposition:** local Issue #7 acceptance PASS; this is not production
approval and does not make VPS-03 complete. Base SHA:
`f266c68958c7d3b512e1f5e036122ab6341bed5e`. Exact package and acceptance
HEAD: `6baa1e8c56c85d9f938866dd924b98d238d75035`, 17 November 2026. PR #6
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
  `70aacce09dbef742979077bf868c2a513213efa7add30aa3569c175ac96f7dc1`.
  Host SHA-256:
  `05a9438005b88b2187e27b882a33c0fcab4016826492d6397c10215a129ccdc1`.
- Publish audit: PE machine `0x014c`, 23 lock entries, 36 published files,
  zero forbidden Local/Server/SQL/MediatR/DbMigrator binaries or secret/SQL
  configuration markers.

## Exact Issue #7 acceptance

The strict runner wrote results beneath
`TestResults/vps03-issue7/6baa1e8c56c85d9f938866dd924b98d238d75035`.
The exact commands were:

```powershell
pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -PreflightOnly
pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -LiveOnly
pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 -AttendedOnly
pwsh -File scripts/Invoke-Vps03Issue7Acceptance.ps1 `
  -LiveResult TestResults/vps03-issue7/6baa1e8c56c85d9f938866dd924b98d238d75035/results/live-result.json `
  -AttendedResult TestResults/vps03-issue7/6baa1e8c56c85d9f938866dd924b98d238d75035/results/attended/attended-result.json
```

| Group | Discovered/executed/passed | Failed | Skipped/not executed |
| --- | ---: | ---: | ---: |
| Crash before/after durable save | 2/2/2 | 0 | 0 |
| Corruption, ACL, origin binding, exact clear | 4/4/4 | 0 | 0 |
| Live restart/actor/grant/session/terminal/operator/unjournalled | 7/7/7 | 0 | 0 |
| Attended Native | 1/1/1 | 0 | 0 |
| Explicit owned-resource cleanup | 1/1/1 | 0 | 0 |
| **Total** | **15/15/15** | **0** | **0** |

The final protected-store case uses two real x86 processes and pauses an exact
clear after validation. A second process cannot clear/save a replacement until
the Owner/SYSTEM-secured global owner/origin mutex serializes the first
compare-delete across console and RDP sessions; the stale clearer
cannot delete the replacement. The live fail-closed matrix first commits and
loses an ACK through the owned IIS/SQL environment, then sends every status
request to the real HTTPS resolution endpoint and mutates only the returned
response. The production DPAPI pending store remains unchanged for all 18
nonterminal, transport, HTTP and identity/contract mismatch results.

The attended run accepted all nine checkpoints: LOGIN, SHELL, CATALOG, GOODS,
SALES, SECURITY, ERRORS, LOGOUT, and UNKNOWN-RECOVERY. The recovery surface
showed the immutable operation identity and no-retry warning, fenced the shell,
enabled only the read-only status check, returned the exact authenticated WEB5
`NOT_EXECUTED` terminal, required a separate confirmation and fresh reread, and
finished with `No pending write` plus disabled recovery actions. Desktop exited
cleanly. Owned child processes, IIS process, SQL database/login, and test-owned
pending intent were absent after teardown. No required case was skipped.
The attended result records both the Desktop executable and Host DLL SHA-256;
the final aggregator requires exact equality with the live package hashes.
Each owned live environment wrote a HEAD-bound teardown ledger only after its
IIS process and exact SQL database/login were absent. The final cleanup group
validated those ledgers plus independent child-process and pending-intent
probes; its booleans were not initialized as assumed PASS values.

Two non-passing development attempts are retained as failures, not counted as
evidence: `5a61e64` ran 6/7 live cases before the new matrix hit the login rate
limit; `51a15cd` had one attended timeout while waiting for operator confirmation.
Reusing one authenticated live session and rerunning the attended walkthrough
within its bounded confirmation interval produced the authoritative results
above.

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
