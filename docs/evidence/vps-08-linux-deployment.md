# VPS-08 — Linux Deployment & Hardening evidence ledger

**Issue:** [#11](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11). **Candidate:** [PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27). **Base main:** 1d3e1f7735da9b069e6feb92cd5b2fb2b88bc19d after VPS-07B merge (2026-11-18).

**Status: SOURCE IMPLEMENTED FOR REVIEW; protected staging NOT VERIFIED.** Final-HEAD source CI, independent review and remote environment execution are distinct. No real VPS, Environment permission, SQL13 staging secrets, restricted runtime or independent Oracle have been supplied in this session; no production release approval. The roadmap README is the only live status authority.

## Submitted source

- Opt-in strict single-hop loopback proxy, secret-admitted header boundary with 127.0.0.1 only, no wildcard KnownProxies or unsafe framework flag. Existing direct HTTPS mode remains default.
- Root-owned systemd/Nginx templates, LoadCredential and unprivileged account, protected audit root, manifest-verified release and atomic binary-only rollback. Review fixes: exclusive flock, trusted owner/mode checks for /opt and release ancestors, rejection of writable/special-mode artifact entries, non-writable installed files, service-active health guard, and full SQL-free real Kestrel forwarded-header/API admission tests.
- Native proxy, SQL TLS and live SQL-free Kestrel regressions (33 cases verified at earlier code HEAD; final HEAD pending), static source/refusal tests, exact SDK 10.0.404 locked publish, and separate manually dispatched protected Environment job.
- Operator-specific Environment provisioning script and [Linux runbook](../runbooks/vps-08-linux-deployment.md).

## Observed review checkpoints — not final deployment acceptance

- **Initial source HEAD `e0e705954039cab22146663ed0452bcc4ee0c53b`:** all **10/10 mandatory PR workflows SUCCESS**, including [VPS-08 #44295115796](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44295115796); original **27/27** native proxy/SQL TLS cases PASS, zero failure/skip, 59-file Linux publish with exact source SHA. Protected staging job was **SKIPPED**.
- **Strengthened proxy code HEAD `7c7e117e40a03c118ee9d5b9f3c30c4a74e4920e`:** [VPS-08 #44308489914](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44308489914) SUCCESS: **33/33** native cases, including 6 real SQL-free Kestrel/forwarded-header/API tests; **0 failed/0 skipped** for the 33 selected tests, 59-file manifest matches that HEAD, published runtime contains **0 Newtonsoft.Json / 0 MediatR**. Other required workflows and the final doc-inclusive HEAD have separate statuses.
- **Review-fix candidate `c35c1b1fd56995aa00e019d0625a5b0047479a73`:** serialized root-only rollout, positive/negative artifact permission checks, host pinning and safe same-SHA reruns implemented. Final aggregate CI on the latest documentation-inclusive HEAD and real staging remain **PENDING / NOT VERIFIED** unless recorded below.

## Findings and resolution (source review only)

| Finding | Severity | Source disposition |
| --- | --- | --- |
| F01 — concurrent rollout and writable ancestor/package could corrupt release state or allow mutable files | P1 | **FIXED IN SOURCE:** `5b2a8ca`: exclusive `flock`, trusted directory modes, no writable/special artifacts, root-owned immutable release, active-service health probe. Actual systemd rollback rehearsal **NOT RUN** |
| F02 — component tests lacked verification that ASP.NET Core consumes forwarded headers before native strict transport admission | P1 | **FIXED AND CI VERIFIED:** `7c7e117`: 6 additional real Kestrel HTTP cases, 33/33 PASS in #44308489914. Real Nginx/TLS remains **NOT RUN** |
| F03 — staging runner label alone could select the wrong machine; direct local bypass lacked staged probe | P1 | **FIXED IN SOURCE:** `1b0fdb2`: reviewed machine-id SHA-256 pin plus two mandatory direct-HTTP deny probes. Protected job **NOT RUN** |
| F04 — failed/partial staging rollout could not be retried on the same reviewed SHA without an operator cleanup | P2 | **FIXED IN SOURCE:** `c35c1b1`: permit same-SHA retry only with identical verified manifest and root-owned existing release, no in-place overwrite. Actual retry/fault rehearsal **NOT RUN** |

Neither an author's code changes nor a second-pass source check is an independent human deployment approval. A protected reviewer must inspect final diff, exact CI and real staging evidence separately.

## Required evidence, never infer PASS

| Gate | Present disposition | Needed to accept |
| --- | --- | --- |
| Static source/shell/manifest refusals | PENDING CI | Exact final HEAD, Python command/log, positive/negative outcome |
| Net10 proxy tests | PENDING CI | TRX 33/33 discovered/executed/passed, failed=0, skipped=0 |
| Linux publish | PENDING CI | SDK10.0.404, locked restore, dependency graph, manifest and SHA artifact |
| Prior Windows/net48, HTTP/SQL, Mediator/FluentMigrator regressions | PENDING CI | All mandatory workflow jobs SUCCESS on final doc-inclusive PR HEAD |
| Protected environment independent review/admin bypass/ref | NOT VERIFIED | Admin config output, reviewer identity, only main allowed, dedicated nonprod runner |
| Genuine SQL13 staging, restricted runtime and separate read-only Oracle/TLS | NOT VERIFIED | SQL source/journal and expected/actual/diff, backup/restore, TLS and restricted-principal proof |
| Real Nginx/systemd/TLS/cert/secret/audit restart | NOT VERIFIED | Host log timestamps, HTTP negative/positive probes, audit retention |
| Host reboot, process loss, failed rollout and rollback | NOT VERIFIED | Actual machine and process events, prior/current SHA, no lost SQL/audit writes |
| Independent review, final HEAD CI, merge and Owner closeout | NOT VERIFIED | Full diff and findings, exact run URLs, explicit acceptance |

PR #9's historical protected job was SKIPPED, NOT PASS; disposable SQL2022 CI is not staged verification. This candidate must not close Issue #11 or mark VPS-08 COMPLETE until all listed operational gates are externally proved. VPS-09 separately owns attended Windows Desktop→deployed Linux Host→SQL E2E and final release authorization.
