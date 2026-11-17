# VPS-04A Merge Boundary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make PR #9 ready for bounded VPS-04A review while preserving truthful follow-up gates for VPS-04B, VPS-08, and VPS-09.

**Architecture:** Keep production code unchanged. Verify R17, create three issue-backed future work boundaries, update the live roadmap/evidence/acceptance descriptions, then require exact-final-HEAD CI before changing the PR from Draft to Ready for review.

**Tech Stack:** Markdown, Git/GitHub CLI, Python static verifiers, .NET Framework 4.8 tests, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-17-vps-04a-merge-boundary-design.md`

## Global Constraints

- `docs/roadmap/README.md` is the only live task-status authority.
- Do not change runtime code, schema, permissions, packages, or frozen fixtures.
- Do not count a skipped staging job as PASS or claim deployment/production readiness.
- Record base/head SHA, commands, counts, failures, CI URLs, and explicit blockers.

## Review Focus

- R17 must reject duplicate JSON members without restoring order sensitivity.
- Decimal wire checks must preserve exactly four fractional digits.
- Follow-up issues must not duplicate existing open issues or move VPS-04A code into later increments.
- Roadmap wording must distinguish review readiness from completion/merge.
- PR readiness must not imply staging, deployment, release, or independent approval.

---

### Task 1: Verify the current code HEAD and R17

**Files:**
- Test: `tests/MyDmsVn.Server.Application.Tests/ContractEnvelopeTests.cs`

**Interfaces:**
- Consumes: PR #9 HEAD and R17 review finding.
- Produces: focused and full Application test evidence for the exact code HEAD.

- [ ] Inspect the R17 commit and confirm strict duplicate-property handling, order independence, date text fidelity, and four-place decimal lexemes.
- [ ] Run locked restore, the focused R17 tests, and the full net48 Application suite; record discovered/executed/failed/skipped counts.
- [ ] Confirm existing GitHub Actions on the exact code HEAD and record any failed or skipped jobs without relabeling them.

### Task 2: Create future-increment issue boundaries

**Files:**
- Modify externally: GitHub issue tracker.

**Interfaces:**
- Consumes: `docs/roadmap/vps-00-vps-09.md` VPS-04B/VPS-08/VPS-09 exit criteria.
- Produces: one issue URL each for portable audit/hosting, deployment/staging, and integrated acceptance/closeout.

- [ ] Search all existing issues to prevent duplicates.
- [ ] Create a VPS-04B issue for portable protected audit, positive WEB6 behavior, middleware/DI/session/authorization, and Linux hosting security regressions.
- [ ] Create a VPS-08 issue for protected staging, systemd, reverse proxy, TLS/secrets, restart/recovery, and VPS deployment/rollback.
- [ ] Create a VPS-09 issue for full 57-route dynamic variants, wider WEB5/fault/race/decimal matrices, Windows Desktop → Linux Host → SQL integration, and explicit release decision.

### Task 3: Reconcile repository authority and evidence

**Files:**
- Modify: `docs/roadmap/README.md`
- Modify: `docs/testing/acceptance-matrix.md`
- Modify: `docs/evidence/vps-04a-replatform.md`
- Modify: `docs/runbooks/vps-04a-selfcontained-ci.md`
- Modify: PR #9 body after the issue URLs and final status exist.

**Interfaces:**
- Consumes: Task 1 evidence and Task 2 issue URLs.
- Produces: a non-self-contradictory VPS-04A review boundary and linked future work.

- [ ] Split the live VPS-04 status into VPS-04A review/merge readiness and VPS-04B follow-up without marking either merged prematurely.
- [ ] Reclassify staging/deployment/integrated acceptance as linked VPS-08/09 gates, not PR #9 code blockers.
- [ ] Record R17 and the boundary decision in evidence/runbook; preserve every unverified fact.
- [ ] Run `git diff --check`, route-ledger verification, protected-gate offline tests, and documentation/path verification commands used by CI.
- [ ] Commit and push the documentary boundary update.

### Task 4: Final-HEAD CI and PR readiness

**Files:**
- Modify externally: PR #9 body/comment/draft state.

**Interfaces:**
- Consumes: exact final documentation HEAD and all triggered workflows.
- Produces: a Ready-for-review PR with exact-SHA evidence and no approval overclaim.

- [ ] Wait for all five workflows on the final documentation HEAD; inspect every conclusion and required test count.
- [ ] Update PR #9 with base/head SHA, exact commands/environment, test counts, failures, CI URLs, follow-up issue links, and explicit remaining review gate.
- [ ] Mark PR #9 Ready for review only if final CI passes and the worktree/remote HEAD agree.
- [ ] Verify the PR is open, mergeable, Ready for review, and not marked approved/merged; report any remaining external reviewer action.
