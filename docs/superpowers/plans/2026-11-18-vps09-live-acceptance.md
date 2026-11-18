# VPS-09 Live Acceptance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add fail-closed tooling and protected workflow gates needed to collect genuine VPS-08 and VPS-09 operational evidence without changing production behavior.

**Architecture:** A committed 293-scenario manifest drives a .NET 10 external acceptance CLI over public HTTPS and an independent read-only SQL Oracle. Linux and Windows operator scripts collect sanitized receipts, while the existing signed-packet verifier and human Owner decision remain separate.

**Tech Stack:** .NET 10, System.Text.Json, Microsoft.Data.SqlClient, xUnit v3, Python 3, PowerShell, Bash, GitHub Actions.

**Spec:** User-approved plan in the 2026-11-18 task conversation.

## Global Constraints

- Preserve authentication, permissions, SQL transactions, inventory/costing, idempotency, JSON wire behavior and schema compatibility.
- Never target production SQL or print credentials/tokens.
- No operational gate is PASS without real protected execution on an exact SHA with zero required skips.
- Raw evidence and signing private keys remain outside Git; only sanitized receipts and hashes may be uploaded.
- Operator differs from Owner/reviewer; self-review and admin bypass are forbidden.

## Review Focus

- Stale or dirty source must fail before network or SQL access.
- Noncanonical HTTPS and unverified SQL TLS must fail closed.
- Missing, duplicate or substituted scenario/oracle evidence must fail closed.
- A recovered failed deployment must still report the original deployment as failed.
- Tools must never synthesize GUI attendance, physical reboot, independent review or Owner approval.

---

### Task 1: Live-scenario manifest and policy validator

- [ ] Add failing tests for 57 routes, 293 unique pairs, executable recipes, Oracle coverage and secret-free content.
- [ ] Add the compact manifest and validator; verify RED then GREEN.
- [ ] Commit the task.

### Task 2: .NET 10 external acceptance CLI

- [ ] Add failing tests for `preflight`, `routes`, `oracles` and `summarize`, including stale SHA, dirty tree, HTTPS/TLS, duplicate/skip, mismatch and cleanup failures.
- [ ] Implement the CLI with injectable HTTP/SQL/filesystem boundaries and sanitized JSON outputs.
- [ ] Run focused and full CLI tests; commit the task.

### Task 3: Linux and Windows operator receipts

- [ ] Add failing executable tests for machine/artifact identity, restart/SIGKILL/reboot resume, failed deploy/rollback, Windows x86 and nine attended checkpoints.
- [ ] Implement Linux receipt and integrated Windows coordinator without production hooks.
- [ ] Run executable script tests; commit the task.

### Task 4: Protected environments and workflows

- [ ] Add failing policy tests for three protected environments, exact-main dispatch and early refusal.
- [ ] Extend configuration helpers and workflows for the CLI and independent review.
- [ ] Run workflow/policy tests; commit the task.

### Task 5: Documentation, regression and handoff

- [ ] Update runbooks/evidence as tooling-only, preserving #11/#12 OPEN until real evidence exists.
- [ ] Run source inventory, Python tests, .NET tests, shell/PowerShell parsing and repository static gates.
- [ ] Perform whole-branch review, fix important findings by TDD, and prepare PR handoff without fabricating runtime acceptance.
