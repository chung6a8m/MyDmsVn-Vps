# VPS-03 Issue 8 acceptance

This walkthrough is a fail-closed, attended check of the exact published x86 Desktop against the owned HTTPS IIS Express host and isolated SQL database. It does not authorize Local composition, certificate bypass, shared databases, or manual deletion of an unresolved Desktop write fence.

## Admission

1. Require a clean Git worktree and record base/head SHA.
2. Publish Desktop x86 and HttpHost from that HEAD, then write package provenance and SHA-256 hashes.
3. Run the acceptance preflight with the approved SQL connection, canonical HTTPS authority and trusted certificate thumbprint.
4. Confirm Task 3 schema 11/12/13 admission and the live API/session/transaction groups have zero failures and zero skips.

## Attended checkpoints

The operator must visibly accept every checkpoint: `VPS03.GUI.LOGIN`, `SHELL`, `CATALOG`, `GOODS`, `SALES`, `SECURITY`, `ERRORS`, and `LOGOUT`. The launcher supplies only the canonical HTTPS origin and operation mode to the Desktop child; SQL, password, JWT, token and secret-shaped environment variables are excluded.

For each checkpoint record the case ID, UTC time, executable hash, process ID and operator decision. Never record credentials, cookies, bearer tokens, SQL connection strings, request bodies, or raw exception text.

## Abort and cleanup

An abort, missing confirmation, non-zero Desktop exit, live IIS process/request queue, unresolved pending intent, or uncertain SQL drop is NOT PASS. Preserve sanitized diagnostics, leave the PR draft and Issue 8 open, and reconcile the exact owned resource before another run. Only a complete final-HEAD run with verified Desktop exit, host absence and SQL absence may emit a passing attended result.
