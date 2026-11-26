# Local full-stack simulation

This runbook operates a disposable MyDmsVn lab on one Windows workstation: SQL Server 2022 in Docker, the `net10.0` Host behind Nginx/systemd in WSL Ubuntu, and the published Windows `net48` Desktop. It uses synthetic users and data only.

> **LOCAL SIMULATION — NOT VPS-08/09 ACCEPTANCE.** A passing run does not verify protected staging, a real VPS, production SQL, reboot/rollback, attended GUI acceptance, or release readiness. It does not change the authoritative roadmap status.

## Owned resources and prerequisites

Run commands from the repository root in PowerShell 7 (`pwsh`). Preflight requires the local Docker Desktop `desktop-linux` named-pipe context with the Linux engine and at least 2 GiB of Docker engine RAM, WSL distribution `Ubuntu` with systemd and noninteractive sudo, .NET SDKs 8.0.428 and 10.0.404, the .NET Framework 4.8 targeting pack, ports 50443/5080/11433 free, and at least 15 GB free. Ubuntu must be able to reach Windows IPv4 loopback for the Docker-published SQL port. On Windows 11 with WSL NAT, use mirrored networking in the workstation WSL settings and restart WSL before running Preflight again.

The default instance state root is `TestResults/local-simulation/active`. When using a custom `-StateRoot`, select a dedicated child directory **inside** `TestResults/local-simulation` (not the parent itself), pass the same `-StateRoot` to every action, and adjust the example artifact/receipt paths below. The commands shown use the default root.

The operator owns only these named resources:

- Docker container `mydmsvn-local-sim-sql` and volume `mydmsvn-local-sim-sql-data`, both labeled with the generated instance ID;
- WSL service/user/directories named `mydmsvn-local-sim`, Nginx site files of the same name, the owned WSL CA entry, and `/opt/mydmsvn-local-sim`;
- the Current User Root CA recorded by thumbprint in `TestResults/local-simulation/active/receipt.json`;
- ignored state below `TestResults/local-simulation`.

The pinned SQL image is Developer edition. The pinned ASP.NET 10.0.11 Linux runtime is SHA-512 verified before installation. Secrets are randomly generated into ignored files with restrictive WSL copies; commands and JSON outcomes never print their values.

## Initialize

```powershell
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Preflight -OutputFormat Json
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Initialize -DryRun -OutputFormat Json
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Initialize -OutputFormat Json
```

Initialization requires a clean Git source tree and an absent or empty state root. It publishes locked artifacts, creates verified TLS, creates and labels the Docker resources, makes a verified local backup of the empty synthetic database, migrates through the dedicated control principal to schema 13, provisions restricted runtime/oracle identities, seeds synthetic fixtures, installs the WSL service, and waits for HTTPS readiness. The backup remains in the same disposable Docker volume; it is a migration admission artifact, not off-host disaster recovery.

The final JSON includes `instanceId`, status `READY`, and `desktopPath`. Do not edit the receipt or secret files.

For a reviewed prebuilt pair, verify the archive digests and source identity against the publication receipts before extracting. Put the verified Desktop and Host payloads in separate `desktop` and `host` children of an absolute local candidate directory. Retain `vps03-desktop-manifest.json` and `vps08-manifest.json` alongside their respective payloads. Pass the reviewed source SHA and the verified SHA-256 of each manifest to both initialization commands.

```powershell
$pair = @{
    CandidateRoot = $verifiedCandidateDirectory
    CandidateSourceSha = $reviewedCandidateSha
    DesktopManifestSha256 = $verifiedDesktopManifestHash
    HostManifestSha256 = $verifiedHostManifestHash
}
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Initialize -DryRun -OutputFormat Json @pair
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Initialize -OutputFormat Json @pair
```

Dry run checks the pair without provisioning. Initialize copies and rechecks the exact pair, publishes the SQL tooling from the clean checkout, and records the candidate separately from the tooling source in the bound receipt. Start and Verify still require the owning tooling checkout. Use the `desktopPath` returned by Initialize for the attended workflow.

## Operate and verify

```powershell
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Start -OutputFormat Json
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Verify -OutputFormat Json
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Stop -OutputFormat Json
```

`Verify` is non-empty: it checks the receipt-bound Docker/WSL/source identity and database ownership/schema/restricted identities; verifies Nginx HTTPS, wrong-Host denial, and direct-Kestrel rejection; authenticates distinct writer and denied users; requires the denied user to receive HTTP 403; writes one synthetic sales receipt over HTTPS and proves the committed business row/journal with the read-only SQL Oracle; then kills the systemd main process and requires a different PID with healthy HTTPS within 30 seconds. Success reports exactly four passed checks and zero failed/skipped checks.

To launch the Desktop checkpoint after `Start`, use the `desktopPath` returned by `Initialize`. The example below uses the default state root:

```powershell
$env:MYDMSVN_DESKTOP_API_ORIGIN = 'https://localhost:50443'
$env:MYDMSVN_DESKTOP_OPERATION_MODE = 'web5'
& .\TestResults\local-simulation\active\artifacts\desktop\MyDmsVn.Desktop.App.exe
```

Confirm the login and representative read/write workflow using only the generated local synthetic credentials under `TestResults/local-simulation/active/secrets`. This is an attended checkpoint: record it separately as PASS/FAIL with the exact HEAD; never infer GUI PASS from publish or automated Verify. Do not copy these credentials to staging or production.

## In-place prebuilt upgrade

Use an initialized prebuilt pair on the same tooling checkout. Close the lab Desktop and recovery client after observing the durable pending operation. Keep the original Windows user and origin. Obtain the expected deployment epoch from the independent SQL observation and the expected pending ciphertext hash from the local pre-upgrade observation. Use the exact owned instance ID and the verified new pair arguments from Initialize.

```powershell
$upgrade = @{
    ConfirmInstanceId = $instanceId
    ExpectedDeploymentEpoch = $observedEpoch
    ExpectedPendingSha256 = $observedPendingHash
}
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action PlanUpgrade -DryRun -ConfirmInstanceId $instanceId -OutputFormat Json @pair
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Upgrade -OutputFormat Json @upgrade @pair
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action VerifyUpgrade -OutputFormat Json @upgrade
```

Plan reports the active and target pair, Host PID, deployment epoch and protected pending file hash. Upgrade takes a read lease on the existing pending file. It retains the previous pair and stages the verified target pair alongside it. It checks SQL and audit identity around the service switch and requires verified HTTPS before recording the new active pair. Launch the returned Desktop executable normally, using the existing lab origin and operation mode, after verifying the upgrade receipt. Observe and reconcile the pending operation before running the separate write exercises.

If interrupted, keep the bound instance and rerun Upgrade with the same expected inputs. To explicitly return to the retained pair while the same fence is present, run:

```powershell
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action RollbackUpgrade -OutputFormat Json @upgrade
```

Keep the lab and diagnostics if an identity check fails. The pending upgrade phase blocks normal lifecycle actions and reset until the cutover is verified or safely rolled back.

## Reset

Read the instance ID, review it, and pass the exact value:

```powershell
$receipt = Get-Content .\TestResults\local-simulation\active\receipt.json -Raw | ConvertFrom-Json
$receipt.instanceId
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Reset -ConfirmInstanceId $receipt.instanceId -OutputFormat Json
```

Reset validates the receipt path, instance ID, Docker engine and labels, WSL marker/source link, and CA file/store subject and thumbprint before any deletion. It refuses missing/malformed receipts, mismatched identities, paths outside the ignored state tree, ancestor or leaf reparse points, and `-DryRun`. It removes only owned resources; unrelated Docker containers, volumes, WSL services, files, and certificates are preserved. A partial failed initialization is reset with the same command only when its acquisition marker proves ownership; otherwise state is retained for diagnosis.

## Troubleshooting

- `Preflight` `BLOCKED`: fix only the named failed check; do not bypass it.
- `StateAlreadyExists`: use the receipt-driven Reset above. An empty state directory is safe and accepted.
- `LockedRestoreFailed`: restore with the exact conditional graph shown in the test commands; do not disable locked mode.
- `Wsl.*Failed`: inspect `systemctl status mydmsvn-local-sim.service`, `journalctl -u mydmsvn-local-sim.service`, and `nginx -t` inside Ubuntu. These surfaces intentionally do not reveal secrets.
- SQL identity/ownership refusal: do not relabel or reuse an existing database/volume. Reset the owned instance and initialize again.

For evidence, record base/head SHA, exact commands and environment, discovered/executed/skipped counts, failures, and explicit blockers. A skipped SQL/GUI check or empty filter is not PASS.

## VPS-10.7 native Host paired handoff

Use the **same reviewed commit** for the published net48 Desktop and Linux net10 Host. Do not substitute a prior candidate or treat an old simulation receipt as final-HEAD evidence.

Before the session, retain the Desktop and Host artifact manifests, exact source SHA, configuration mode (without secrets), the isolated SQL instance/schema identity, and the `--list-routes` JSON. Compare the latter with the existing frozen v1/v2/v3 route manifests using `scripts/verify-vps-04a.py`; require 57 native / 0 bridge. Check the published Host graph using `python3 scripts/verify-vps04b-runtime-boundary.py --artifact <published-host.deps.json>`. In a disposable Development instance verify OpenAPI against all 57 verb/path pairs; never expose Development OpenAPI in Production.

Exercise from the actual Windows Desktop: login/refresh/logout, lookup, Sales draft/create/update/post/read, Receipt draft/create/update/post/read, inventory balances and stock card, security/permission denial, and reopen a persisted unresolved operation after lost ACK/restart. Keep the operation identity, fingerprint, deployment epoch, receipt and authoritative outcome; never auto-resend an unsafe/unknown operation. Use synthetic data, record exact observed outcomes and SQL invariants, and retain cleanup receipts.

Re-exercise the pending-operation upgrade cases U01–U06 from [Issue #45](https://github.com/chung6a8m/MyDmsVn-Vps/issues/45) with the exact reviewed pair and distinguish automated HTTP/SQL evidence from attended Desktop observation. The earlier PR #44 source-only acceptance does not waive the VPS-10.7 paired gate. Record blockers as NOT VERIFIED and leave the live status in the [roadmap index](../roadmap/README.md).
