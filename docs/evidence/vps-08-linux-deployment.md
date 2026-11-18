# VPS-08 — Linux Deployment & Hardening evidence ledger

**Issue:** [#11](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11). **Source-only merged:** [PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27). **Base main:** 1d3e1f7735da9b069e6feb92cd5b2fb2b88bc19d after VPS-07B merge (2026-11-18).

**Status: SOURCE-ONLY MERGED on 2026-11-18 (explicit Owner approval); protected staging NOT VERIFIED.** Final-HEAD source CI, independent review and remote environment execution are distinct. No real VPS, Environment permission, SQL13 staging secrets, restricted runtime or independent Oracle have been supplied in this session; no production release approval. The roadmap README is the only live status authority.

## Submitted source

- Opt-in strict single-hop loopback proxy, secret-admitted header boundary with 127.0.0.1 only, no wildcard KnownProxies or unsafe framework flag. Existing direct HTTPS mode remains default.
- Root-owned systemd/Nginx templates, LoadCredential and unprivileged account, protected audit root, manifest-verified release and atomic binary-only rollback. Review fixes: exclusive flock, trusted owner/mode checks for /opt and release ancestors, rejection of writable/special-mode artifact entries, non-writable installed files, exact systemd MainPID release-cwd health guard, Bash credential-path evaluation fix, and full SQL-free real Kestrel forwarded-header/API admission tests. The new executable shell regressions require live loading of all three systemd credentials and rejection of injected-newline/symlink credentials, stale process and invalid MainPID.
- Native proxy, SQL TLS and live SQL-free Kestrel regressions (33/33 PASS on final reviewed source HEAD), static source/refusal tests, exact SDK 10.0.404 locked publish, and separate manually dispatched protected Environment job.
- Operator-specific Environment provisioning script and [Linux runbook](../runbooks/vps-08-linux-deployment.md).

## VPS-09 acceptance-tooling extension (pending PR review)

The current tooling branch adds an exclusive, SHA/machine/artifact-bound Linux receipt state machine and initializes it from the protected VPS-08 workflow. It requires all eight VPS08-01..08 event families; an injected failed deployment is recorded as `EXPECTED_FAILURE_RECOVERED`, never silently converted to PASS by a successful rollback. The receipt hashes protected external evidence and deliberately ends as `STRUCTURE_COMPLETE_NOT_REVIEWED` with physical review `REQUIRED`.

This source change does not alter the disposition below. No protected runner, SQL13 instance, reboot, rollback or independent review was executed in this implementation session. Issue #11 remains OPEN and every real staging row remains NOT VERIFIED until an exact main candidate is run and reviewed.

## Observed review checkpoints — not final deployment acceptance

- **Initial source HEAD `e0e705954039cab22146663ed0452bcc4ee0c53b`:** all **10/10 mandatory PR workflows SUCCESS**, including [VPS-08 #44295115796](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44295115796); original **27/27** native proxy/SQL TLS cases PASS, zero failure/skip, 59-file Linux publish with exact source SHA. Protected staging job was **SKIPPED**.
- **Strengthened proxy code HEAD `7c7e117e40a03c118ee9d5b9f3c30c4a74e4920e`:** [VPS-08 #44308489914](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44308489914) SUCCESS: **33/33** native cases, including 6 real SQL-free Kestrel/forwarded-header/API tests; **0 failed/0 skipped** for the 33 selected tests, 59-file manifest matches that HEAD, published runtime contains **0 Newtonsoft.Json / 0 MediatR**. Other required workflows and the final doc-inclusive HEAD have separate statuses.
- **Review-fix candidate `c35c1b1fd56995aa00e019d0625a5b0047479a73`:** serialized root-only rollout, positive/negative artifact permission checks, host pinning and safe same-SHA reruns implemented. This was a historical checkpoint; see final source-only closeout below. Real protected staging remains **NOT VERIFIED**.

## Source-only merge closeout (2026-11-18)

- **Owner decision:** approve **source-only repository merge**, not independent protected staging / production readiness. [Recorded on PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27#issuecomment-6980385677).
- **Reviewed source HEAD:** `516e4cc9ffd378412b2a370b04ab269a6f49dc70`; **merge commit:** [`628aaf994556595f5987286e84ab7491b9054c50`](https://github.com/chung6a8m/MyDmsVn-Vps/commit/628aaf994556595f5987286e84ab7491b9054c50). Independent source review F05–F07, regression fixes and preserved F01–F04 documented in [PR review report](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27#issuecomment-6980129481).
- **10/10 mandatory workflows SUCCESS on that exact source HEAD**, including [VPS-08 artifact/proxy #44314305206](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44314305206), [disposable HTTPS→SQL13 #44314305227](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44314305227), [SQL journal bridge #44314305459](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44314305459) and [Windows xUnit #44314305343](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44314305343). `source-gates` and `linux-publish` SUCCESS: shell credential and stale-release negative fixtures, 33/33 native cases, 59-file SHA-bound package, 0 published Newtonsoft/MediatR. Ephemeral SQL2022: 15/15 HTTPS cases, separate WEB6 3/3 and journal 58/58; **not protected staging evidence**.
- **Explicit unfulfilled gates:** protected `vps08-sql13-nonproduction` job **SKIPPED**; independent human staging reviewer, protected Environment, dedicated runner, isolated SQL13 TLS/read-only Oracle, real systemd/Nginx/credentials/WEB6 audit and reboot/fault rollback/reconciliation **NOT VERIFIED**. **Keep Issue #11 OPEN**; VPS-09 still owns full Desktop-to-Linux-to-SQL E2E and final production release decision.

## Findings and resolution (source review only)

| Finding | Severity | Source disposition |
| --- | --- | --- |
| F01 — concurrent rollout and writable ancestor/package could corrupt release state or allow mutable files | P1 | **FIXED IN SOURCE:** `5b2a8ca`: exclusive `flock`, trusted directory modes, no writable/special artifacts, root-owned immutable release, active-service health probe. Actual systemd rollback rehearsal **NOT RUN** |
| F02 — component tests lacked verification that ASP.NET Core consumes forwarded headers before native strict transport admission | P1 | **FIXED AND CI VERIFIED:** `7c7e117`: 6 additional real Kestrel HTTP cases, 33/33 PASS in #44308489914. Real Nginx/TLS remains **NOT RUN** |
| F03 — staging runner label alone could select the wrong machine; direct local bypass lacked staged probe | P1 | **FIXED IN SOURCE:** `1b0fdb2`: reviewed machine-id SHA-256 pin plus two mandatory direct-HTTP deny probes. Protected job **NOT RUN** |
| F04 — failed/partial staging rollout could not be retried on the same reviewed SHA without an operator cleanup | P2 | **FIXED IN SOURCE:** `c35c1b1`: permit same-SHA retry only with identical verified manifest and root-owned existing release, no in-place overwrite. Actual retry/fault rehearsal **NOT RUN** |
| F05 — Bash `local target="$1" name="$2" file="$CREDENTIALS_DIRECTORY/$name"` expands `$name` before assignment, preventing systemd credential loading and startup | P0 | **FIXED IN SOURCE:** `de245b2`: assign `name` before building the file path; executable shell fixture added in `6e911d8`. Final source HEAD CI **VERIFIED 10/10**; real systemd startup **NOT VERIFIED** |
| F06 — public TLS and active service alone can accept a stale binary/process after rollout | P1 | **FIXED IN SOURCE:** `3eebcb6`: require systemd MainPID with `/proc/PID/cwd` resolving to the exact selected release, both on deploy and rollback. Executable negative simulation added in `6e911d8`; real fault injection **NOT RUN** |
| F07 — ignored `systemctl restart` exit code could report same-SHA retry healthy without applying updated runtime settings | P1 | **FIXED IN SOURCE:** `f239be2`: require successful restart in addition to exact-process/HTTPS probe; recovery success requires restart success. Static source regression added in `f617b45`; actual service fault injection **NOT RUN** |

Neither an author's code changes nor a second-pass source check is an independent human deployment approval. A protected reviewer must inspect final diff, exact CI and real staging evidence separately.

## Required evidence, never infer PASS

| Gate | Present disposition | Needed to accept |
| --- | --- | --- |
| Static source/shell/manifest refusals, credential/runtime process simulations | **PASS — final source HEAD** | [VPS-08 #44314305206](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44314305206), positive/negative shell fixture and artifact policy |
| Net10 proxy tests | **PASS — 33/33, no failure/skip** | [VPS-08 #44314305206](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44314305206), native TRX |
| Linux publish | **PASS — source-only artifact** | SDK10.0.404, locked restore, 59/59 files, exact source SHA; no protected VPS deployed |
| Prior Windows/net48, HTTP/SQL, Mediator/FluentMigrator regressions | **PASS — 10/10 source-HEAD workflows** | [Complete workflow list](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27#issuecomment-6980129481); ephemeral SQL only |
| Protected environment independent review/admin bypass/ref | NOT VERIFIED | Admin config output, reviewer identity, only main allowed, dedicated nonprod runner |
| Genuine SQL13 staging, restricted runtime and separate read-only Oracle/TLS | NOT VERIFIED | SQL source/journal and expected/actual/diff, backup/restore, TLS and restricted-principal proof |
| Real Nginx/systemd/TLS/cert/secret/audit restart | NOT VERIFIED | Host log timestamps, HTTP negative/positive probes, audit retention |
| Host reboot, process loss, failed rollout and rollback | NOT VERIFIED | Actual machine and process events, prior/current SHA, no lost SQL/audit writes |
| Source review, final HEAD CI, Owner source-only authorization and merge | **SOURCE-ONLY COMPLETE / MERGED** | PR #27 final reviewed HEAD + merge commit; **independent human staging acceptance NOT VERIFIED** |

PR #9's historical protected job was SKIPPED, NOT PASS; disposable SQL2022 CI is not staged verification. Source-only merge does not close Issue #11 or mark VPS-08 operationally COMPLETE until all listed staging gates are externally proved. VPS-09 separately owns attended Windows Desktop→deployed Linux Host→SQL E2E and final release authorization.
