# Local Full-Stack Simulation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a safe, repeatable local Windows + WSL2 + Docker simulation of the MyDmsVn deployment.

**Architecture:** PowerShell owns the operator lifecycle and receipts. A bounded .NET helper owns SQL creation, schema/principal/fixture verification, while a Bash helper installs and verifies the WSL systemd/Nginx boundary. Generated secrets and artifacts stay in ignored state.

**Tech Stack:** PowerShell 7/Windows PowerShell, Bash, Python unittest policy tests, .NET 10, SQL Server 2022 Docker, WSL2 systemd, Nginx.

**Spec:** `docs/superpowers/specs/2026-11-19-local-fullstack-simulation-design.md`

## Global Constraints

- Never mutate or target production SQL, VPS, GitHub Environments, or production deployment paths.
- Container, volume, service, user, database and WSL paths use the `mydmsvn-local-sim` identity.
- All generated credentials remain outside Git under `TestResults/local-simulation` or root-owned WSL paths and are redacted from output.
- `Reset` is fail-closed and receipt-bound; unrelated Docker/WSL resources are preserved.
- Local results are explicitly not VPS-08/VPS-09 evidence.

## Review Focus

- A forged, missing, or symlinked receipt must make destructive actions refuse.
- Port/name/path collisions must refuse rather than reuse unrelated resources.
- Re-running Initialize against existing SQL state must verify ownership and avoid re-seeding.
- Logs and JSON reports must not contain generated passwords, JWT keys, proxy tokens, or connection strings.
- Partial initialization and failed recovery must remain visibly failed and safely resettable.

---

### Task 1: Operator contract, preflight, and reset safety

**Files:**
- Create: `scripts/local-simulation/Invoke-MyDmsVnLocalSimulation.ps1`
- Create: `scripts/tests/test_local_simulation.py`

**Interfaces:**
- Produces: `-Action Preflight|Initialize|Start|Verify|Stop|Reset`, `-StateRoot`, JSON receipt/report files, and fail-closed Reset admission.

- [ ] Write subprocess tests for preflight JSON, missing/forged receipt refusal, symlink refusal, and unrelated-resource preservation; run and observe missing-script failures.
- [ ] Implement parameter validation, non-mutating preflight, receipt loading/validation, redaction, and Reset orchestration.
- [ ] Run `python -m unittest scripts.tests.test_local_simulation -v`; expected all pass.
- [ ] Commit Task 1.

### Task 2: Synthetic SQL lifecycle

**Files:**
- Create: `tools/MyDmsVn.LocalSimulation/*`
- Create: `tests/MyDmsVn.LocalSimulation.Tests/*`
- Modify: Task 1 orchestrator.

**Interfaces:**
- Consumes: receipt instance ID/state root and Docker identity from Task 1.
- Produces: `create`, `provision`, `verify`, and `reset-admission` commands with JSON-safe results; exact database `MyDmsVnVps_LocalSimulation` and distinct principals.

- [ ] Write admission and provisioning-policy unit tests first; observe failures.
- [ ] Implement the minimal net10 helper and wire Initialize/Start/Verify.
- [ ] Run the focused .NET tests and Python lifecycle tests; expected all pass.
- [ ] Commit Task 2.

### Task 3: WSL systemd, Nginx, publish, and recovery drills

**Files:**
- Create: `scripts/local-simulation/mydmsvn-local-sim`
- Create: local-only templates beneath `scripts/local-simulation/templates/`
- Modify: Task 1 orchestrator and policy tests.

**Interfaces:**
- Consumes: receipt, generated TLS material, published Host/Desktop, and SQL runtime configuration.
- Produces: `mydmsvn-local-sim.service`, localhost HTTPS `50443`, Kestrel `127.0.0.1:5080`, and start/stop/verify/reset operations.

- [ ] Write sandbox tests for install-path admission, generated config, proxy negatives, recovery result reporting, and cleanup ownership; observe failures.
- [ ] Implement WSL install/start/verify/stop/reset and publish wiring without changing production templates.
- [ ] Run policy tests, Bash syntax checks, focused native Host tests, and dry-run rendering; expected all pass.
- [ ] Commit Task 3.

### Task 4: Runbook and final verification

**Files:**
- Create: `docs/runbooks/local-fullstack-simulation.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: all operator commands from Tasks 1-3.
- Produces: prerequisites, lifecycle, attended Desktop checkpoint, recovery drills, troubleshooting, evidence limits, and cleanup procedure.

- [ ] Document exact commands and safety boundaries.
- [ ] Run all new tests, existing Python suite in WSL, static repository checks, locked restores/builds for changed .NET projects, and secret/path scans.
- [ ] Perform final whole-branch review and fix Critical/Important findings via RED→GREEN.
- [ ] Commit Task 4.

