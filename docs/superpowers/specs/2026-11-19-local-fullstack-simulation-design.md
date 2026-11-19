# Local Full-Stack Simulation Design

## Intent

Provide a repeatable, nonproduction lab on one Windows workstation. The Windows `net48/x86` Desktop talks through local HTTPS to the Linux `net10` Host running under WSL2; SQL Server 2022 runs in Docker with persistent synthetic data. The lab is for development and operator rehearsal only and never satisfies VPS-08 or VPS-09 acceptance.

## Selected approach

Use the approved hybrid layout: Windows Desktop, WSL2 systemd/Nginx/Kestrel, and Docker SQL Server. An all-Docker layout would weaken systemd and process-recovery drills; a Windows-hosted server would skip the Linux boundary.

The repository supplies one PowerShell entry point with `Preflight`, `Initialize`, `Start`, `Verify`, `Stop`, and `Reset` actions. Supporting Bash and .NET tooling remain local-simulation-specific and never relax production deployment admission.

## Invariants

- Use only synthetic data in `MyDmsVnVps_LocalSimulation`.
- Bind SQL and HTTPS locally; never expose the lab to LAN or Internet.
- Use distinct migration, runtime, and read-only Oracle SQL identities with verified TLS.
- Keep secrets and generated state below ignored `TestResults/local-simulation`; never print secret values.
- Keep WSL paths, users, service names, Docker names, and volumes under a `mydmsvn-local-sim` prefix.
- `Stop` preserves SQL data. Only explicit, receipt-bound `Reset` removes it.
- Preserve production templates and all current auth, permission, transaction, inventory, costing, idempotency, and wire behavior.
- Every report states `LOCAL SIMULATION — NOT VPS-08/09 ACCEPTANCE`.

## Success criteria

Preflight reports actionable capability results without mutation. Initialize creates a receipt-bound lab, publishes the existing applications, provisions schema13 and least-privilege identities, and installs the local WSL service/proxy. Start and Verify refuse drift. Verify covers HTTPS health, direct/proxy denial, representative authenticated API/SQL behavior, service restart and SIGKILL recovery. Reset proves ownership before deleting only lab-owned resources.

