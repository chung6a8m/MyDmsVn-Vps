# VPS-03 Issue #8 Acceptance Harness Design

## Intent

Issue #8 must turn the VPS-03 source implementation in PR #6 into reproducible
acceptance evidence from an exact PR HEAD. The evidence must exercise the real
Windows `net48` x86 Desktop through the configured HTTPS `HttpHost` into an
operator-approved isolated SQL Server. It must not promote mocked HTTP tests,
static dependency checks, test discovery, or an unattended build into an E2E or
GUI PASS.

## Goals

- Add a repository-owned, opt-in VPS-03 acceptance harness with strict preflight,
  non-empty discovery, explicit case inventory, sanitized machine-readable results,
  and a human-readable evidence summary.
- Exercise all 46 remote Desktop API methods through the production HTTP adapter,
  including positive, validation, authentication, authorization, conflict, and
  revision-mismatch behavior required by issue #8.
- Exercise login/me/logout, refresh-cookie rotation, account disable, idle expiry,
  cancellation, mismatched Origin, and two-session permission/session boundaries.
- Prove real SQL rollback, audit/journal/fingerprint, inventory ledger, stock, COGS,
  idempotency, lost-ACK readback, and no duplicate Post behavior.
- Launch the exact published Desktop artifact for attended UI acceptance covering
  login, shell/workspaces, error mapping, UNKNOWN recovery, and resource lifetime.
- Record base/head SHA, package hashes, sanitized environment identity, SQL schema
  fingerprint, commands, inventory, discovered/executed/skipped/failed counts,
  cleanup outcome, and CI URLs without writing credentials or tokens.

## Non-goals and anti-goals

- Do not retarget production projects, replace DbUp/MediatR/Newtonsoft, change JSON
  contracts, remove the historical Local adapter, or rename namespaces.
- Do not place SQL credentials/providers in the Desktop package or process.
- Do not run automatically on ordinary GitHub-hosted CI, silently choose a SQL
  instance, bypass TLS validation, substitute HTTP for HTTPS, or infer production
  activation/merge approval.
- Do not call skipped required cases, empty filters, source-only coverage, or a
  build-only result PASS.
- Do not mutate an unapproved or production database. The harness creates uniquely
  owned test databases through the existing approved test connection contract and
  verifies cleanup.

## Constraints

- Preserve current authentication, permission, transaction, inventory/costing,
  idempotency, wire, and schema behavior.
- Run on attended Windows with .NET Framework 4.8 and x86 Desktop bytes built from
  the exact PR HEAD.
- Use an actual HTTPS HttpHost at one canonical configured origin with
  `MYDMSVN_WEB_DESKTOP_API=enabled`; certificate validation remains enabled.
- Treat schema 11, 12, and 13 as distinct required paths and use a restricted
  runtime principal after administrative provisioning.
- Keep secrets out of command output, TRX, JSON, Markdown, process arguments, and
  committed files.

## Approaches considered

1. **Dedicated VPS-03 harness (selected).** Add a focused test project and an
   orchestration script that use the production HTTP adapter, a separately hosted
   packaged server, isolated SQL oracles, and an attended UI phase. This gives the
   issue its own truthful inventory and evidence boundary.
2. **Retrofit the legacy packaged GUI suite.** This reuses substantial UIA and SQL
   code, but its project graph and package assumptions include Local/Server runtime
   composition. Converting it in place risks confusing historical local acceptance
   with the VPS-03 HTTP boundary.
3. **Manual runbook only.** This is smaller but cannot reliably prove 46-method
   coverage, non-empty execution, two-session/idempotency invariants, or repeatable
   final-HEAD evidence.

## Design

### Acceptance project

Create an opt-in Windows `net48` x86 acceptance test project dedicated to issue #8.
It will reference the production Desktop Application/HTTP assemblies but never the
Local adapter. Test-only provisioning/oracle code may use server-side SQL helpers;
those dependencies stay in the acceptance runner and are excluded from the Desktop
artifact.

The project will organize cases by contract inventory rather than UI screen:

- environment/package/TLS/schema preflight;
- 46-method production HTTP adapter matrix;
- authentication, sessions, permissions, Origin and cancellation;
- Sales/Goods transactions, concurrency, idempotency, lost ACK and recovery;
- SQL journal/ledger/stock/COGS and rollback oracles;
- attended Desktop UI and process/resource-lifetime checks.

Every required case will have a stable ID. A checked-in manifest maps each of the
46 methods and each issue #8 acceptance row to at least one case. A contract test
fails if the manifest loses a method, duplicates an identity unexpectedly, or maps
to a missing test.

### Orchestration and safety

Add a PowerShell entry point that performs read-only preflight before any database
creation: clean/exact HEAD identity, x86 package provenance, canonical HTTPS origin,
trusted certificate, enabled Desktop API, required environment variables, and
operator acknowledgement. It will refuse localhost shortcuts that do not match the
configured public origin and refuse absent/ambiguous database ownership metadata.

After preflight, the runner provisions uniquely named databases for the required
schema paths, starts or validates the packaged HttpHost with only its restricted
runtime connection, runs automated cases sequentially where SQL resources are
shared, pauses for clearly identified attended GUI cases, verifies cleanup, and
then emits results. Administrative SQL values are supplied only to the harness;
Desktop child processes receive the HTTPS origin/mode and no SQL variables.

The harness will fail closed on missing TRX/results, zero discovered or executed
tests, any required skip/not-executed case, a package/SHA mismatch, cleanup failure,
or incomplete manifest coverage.

### Evidence

Generated output goes to an ignored results directory. It contains a sanitized JSON
record, TRX files, package hashes, SQL fingerprints/oracles, and a Markdown draft.
Only reviewed, secret-free summaries and immutable run URLs are copied into
`docs/evidence/vps-03-desktop-http-cutover.md` and the PR body. The roadmap moves
from `IN PROGRESS` only when every issue #8 gate passes on the same final HEAD;
otherwise each failed, skipped, or unavailable gate remains explicit.

## Verification

- TDD contract tests first prove the 46-method/case manifest and preflight refusal
  behavior.
- SQL-free unit tests cover parsing, sanitization, result aggregation, and child
  process environment isolation.
- The final local acceptance command must report exact discovered/executed/skipped/
  failed totals and preserve all raw evidence.
- Existing Architecture, Desktop.Session, Application, HttpHost, discovery, and
  published-artifact gates are rerun on the final code HEAD.
- GitHub Actions is rerun on the final documentation HEAD after accepted evidence is
  recorded. Until then PR #6 stays draft and issue #8 stays open.

