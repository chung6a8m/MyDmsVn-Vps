# VPS-08 — Linux Deployment & Hardening evidence ledger

**Issue:** [#11](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11). **Candidate:** [PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27). **Base main:** 1d3e1f7735da9b069e6feb92cd5b2fb2b88bc19d after VPS-07B merge (2026-11-18).

**Status: SOURCE IMPLEMENTED FOR REVIEW; protected staging NOT VERIFIED.** Final-HEAD source CI, independent review and remote environment execution are distinct. No real VPS, Environment permission, SQL13 staging secrets, restricted runtime or independent Oracle have been supplied in this session; no production release approval. The roadmap README is the only live status authority.

## Submitted source

- Opt-in strict single-hop loopback proxy, secret-admitted header boundary with 127.0.0.1 only, no wildcard KnownProxies or unsafe framework flag. Existing direct HTTPS mode remains default.
- Root-owned systemd/Nginx templates, LoadCredential and unprivileged account, protected audit root, manifest-verified release and atomic binary-only rollback.
- Native boundary security regressions (23 expected), static source/refusal tests, exact SDK 10.0.404 locked publish, and separate manually dispatched protected Environment job.
- Operator-specific Environment provisioning script and [Linux runbook](../runbooks/vps-08-linux-deployment.md).

## Required evidence, never infer PASS

| Gate | Present disposition | Needed to accept |
| --- | --- | --- |
| Static source/shell/manifest refusals | PENDING CI | Exact final HEAD, Python command/log, positive/negative outcome |
| Net10 proxy tests | PENDING CI | TRX 23/23 discovered/executed/passed, failed=0, skipped=0 |
| Linux publish | PENDING CI | SDK10.0.404, locked restore, dependency graph, manifest and SHA artifact |
| Prior Windows/net48, HTTP/SQL, Mediator/FluentMigrator regressions | PENDING CI | All mandatory workflow jobs SUCCESS on final doc-inclusive PR HEAD |
| Protected environment independent review/admin bypass/ref | NOT VERIFIED | Admin config output, reviewer identity, only main allowed, dedicated nonprod runner |
| Genuine SQL13 staging, restricted runtime and separate read-only Oracle/TLS | NOT VERIFIED | SQL source/journal and expected/actual/diff, backup/restore, TLS and restricted-principal proof |
| Real Nginx/systemd/TLS/cert/secret/audit restart | NOT VERIFIED | Host log timestamps, HTTP negative/positive probes, audit retention |
| Host reboot, process loss, failed rollout and rollback | NOT VERIFIED | Actual machine and process events, prior/current SHA, no lost SQL/audit writes |
| Independent review, final HEAD CI, merge and Owner closeout | NOT VERIFIED | Full diff and findings, exact run URLs, explicit acceptance |

PR #9's historical protected job was SKIPPED, NOT PASS; disposable SQL2022 CI is not staged verification. This candidate must not close Issue #11 or mark VPS-08 COMPLETE until all listed operational gates are externally proved. VPS-09 separately owns attended Windows Desktop→deployed Linux Host→SQL E2E and final release authorization.
