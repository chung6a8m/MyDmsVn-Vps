# VPS-09 Single-Owner Closeout Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the explicitly approved single-Owner governance mode needed to collect genuine VPS-08/VPS-09 operational evidence, while preserving every technical acceptance gate and refusing to claim completion without real infrastructure evidence.

**Architecture:** OWN-VPS-04 records the scoped governance exception. Packet schema v2, the environment policy, PowerShell coordinator and signed verifier bind that exception to one Owner, two distinct runner identities, a pre-pinned Ed25519 key and a 24-hour cooling-off period. Runtime acceptance and issue closure remain separate operational phases and cannot be synthesized by source tests.

**Tech Stack:** Python 3, PowerShell, GitHub Actions, JSON, OpenSSL Ed25519, .NET 10 tests.

**Spec:** User-approved plan in the 2026-11-19 task conversation.

## Global Constraints

- Preserve authentication, permissions, SQL transactions, inventory/costing, idempotency, JSON wire behavior and schema compatibility.
- OWN-VPS-04 applies only to VPS08-01..08 and VPS09-01..14 in the Issue #12 closeout candidate.
- Never represent the single-Owner path as independent human review.
- Keep 57 routes, 293 live variants, 12 SQL Oracles, 8 Linux events, 9 GUI checkpoints, 14 gates and 11 exact-SHA workflows; required skips remain zero.
- No production SQL, production deployment, fabricated evidence, private signing key or secret may enter Git.

## Review Focus

- Legacy schema-v1 or mixed-governance packets must fail closed.
- The 24-hour cooling period must use strict UTC timestamps and reject future or malformed values.
- First- and second-pass runner identities must be distinct and cryptographically bound.
- GitHub Environments must have exact-main admission, no reviewer under OWN-VPS-04 and admin bypass disabled.
- Source tooling must continue to refuse stale SHA, skipped gates, evidence swaps, symlinks and key mismatches.

---

### Task 1: Governance decision and signed-packet schema v2

- [ ] Add failing verifier tests for the single-Owner schema, decision, key, timestamps and runner separation.
- [ ] Add OWN-VPS-04 and update the decision register.
- [ ] Implement schema-v2 verification while retaining all existing evidence gates.
- [ ] Run focused and full verifier tests, then commit.

### Task 2: Environment, workflow and coordinator single-Owner admission

- [ ] Add failing tests for zero-reviewer exact-main Environment policy and one-Owner coordinator admission.
- [ ] Update the configuration helper, PowerShell coordinator and workflow interfaces.
- [ ] Preserve distinct application actors and all physical evidence requirements.
- [ ] Run Python, PowerShell/parser and .NET acceptance suites, then commit.

### Task 3: Documentation, inventories and source closeout

- [ ] Update gate registry, runbooks, evidence and roadmap to describe tooling prepared but operational acceptance still blocked.
- [ ] Reconcile Issue #11 historical state in documentation without claiming operational PASS.
- [ ] Run repository inventories and all source verification suites.
- [ ] Perform whole-branch review and prepare the governance/tooling PR; do not configure live environments, sign a packet or close issues without real evidence.

### Operational Phase (external prerequisite, not source implementation)

- [ ] Provision the four approved nonproduction machines/services and three GitHub Environments.
- [ ] Freeze the candidate and run all 11 exact-SHA workflows, VPS08-01..08 and VPS09-01..14.
- [ ] Finalize evidence, wait 24 hours, issue the Owner release decision, sign and second-pass verify the packet.
- [ ] Merge the evidence-only closeout PR, then close Issues #11 and #12 as completed.
