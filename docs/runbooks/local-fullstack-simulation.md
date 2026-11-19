# Local full-stack simulation

This runbook operates a disposable MyDmsVn lab on one Windows workstation: SQL Server 2022 in Docker, the `net10.0` Host behind Nginx/systemd in WSL Ubuntu, and the published Windows `net48` Desktop. It uses synthetic users and data only.

> **LOCAL SIMULATION — NOT VPS-08/09 ACCEPTANCE.** A passing run does not verify protected staging, a real VPS, production SQL, reboot/rollback, attended GUI acceptance, or release readiness. It does not change the authoritative roadmap status.

## Owned resources and prerequisites

Run commands from the repository root in a normal PowerShell session. Preflight requires the local Docker Desktop `desktop-linux` named-pipe context with the Linux engine, WSL distribution `Ubuntu` with systemd and noninteractive sudo, .NET SDKs 8.0.428 and 10.0.404, the .NET Framework 4.8 targeting pack, ports 50443/5080/11433 free, and at least 15 GB free.

The operator owns only these named resources:

- Docker container `mydmsvn-local-sim-sql` and volume `mydmsvn-local-sim-sql-data`, both labeled with the generated instance ID;
- WSL service/user/directories named `mydmsvn-local-sim`, Nginx site files of the same name, the owned WSL CA entry, and `/opt/mydmsvn-local-sim`;
- the Current User Root CA recorded by thumbprint in `TestResults/local-simulation/receipt.json`;
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

## Operate and verify

```powershell
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Start -OutputFormat Json
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Verify -OutputFormat Json
.\scripts\local-simulation\Invoke-MyDmsVnLocalSimulation.ps1 -Action Stop -OutputFormat Json
```

`Verify` is non-empty: it checks the receipt-bound Docker/WSL/source identity and database ownership/schema/restricted identities; verifies Nginx HTTPS, wrong-Host denial, and direct-Kestrel rejection; authenticates distinct writer and denied users; requires the denied user to receive HTTP 403; writes one synthetic sales receipt over HTTPS and proves the committed business row/journal with the read-only SQL Oracle; then kills the systemd main process and requires a different PID with healthy HTTPS within 30 seconds. Success reports exactly four passed checks and zero failed/skipped checks.

To launch the Desktop checkpoint after `Start`, use the path returned by `Initialize`:

```powershell
$env:MYDMSVN_DESKTOP_API_ORIGIN = 'https://localhost:50443'
$env:MYDMSVN_DESKTOP_OPERATION_MODE = 'web5'
& .\TestResults\local-simulation\artifacts\desktop\MyDmsVn.Desktop.App.exe
```

Confirm the login and representative read/write workflow using only the generated local synthetic credentials under `TestResults/local-simulation/secrets`. This is an attended checkpoint: record it separately as PASS/FAIL with the exact HEAD; never infer GUI PASS from publish or automated Verify. Do not copy these credentials to staging or production.

## Reset

Read the instance ID, review it, and pass the exact value:

```powershell
$receipt = Get-Content .\TestResults\local-simulation\receipt.json -Raw | ConvertFrom-Json
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
