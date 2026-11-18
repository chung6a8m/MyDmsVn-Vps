# VPS-09 — integrated acceptance & final release evidence ledger

Issue [#12](https://github.com/chung6a8m/MyDmsVn-Vps/issues/12). Independent source repository `chung6a8m/MyDmsVn-Vps`.
**Status as of 2026-11-18: SOURCE IMPLEMENTATION PREPARED / RUNTIME AND OWNER RELEASE NOT VERIFIED.** This file records the acceptance requirements, not fabricated operational output. [Canonical roadmap](../roadmap/README.md) is the sole live status authority.

## Baseline, not transferable signoff

- VPS-08 source-only merge [PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27), reviewed code HEAD `516e4cc9ffd378412b2a370b04ab269a6f49dc70`, merge `628aaf994556595f5987286e84ab7491b9054c50`; **10/10 required source workflows** and 33/33 native tests. Protected staging job SKIPPED, Issue #11 OPEN.
- VPS-07B [PR #26](https://github.com/chung6a8m/MyDmsVn-Vps/pull/26), isolated SQL2022 verified TLS 58/58 migrations/faults; not a deployed business SQL cutover.
- VPS-05C [PR #17](https://github.com/chung6a8m/MyDmsVn-Vps/pull/17), 293/293 SQL-free live net48↔net10 HTTP observations across 57 frozen routes; **not** 293 real-SQL observations.
- VPS-03 Issue #8 captured attended Windows Desktop→legacy HTTPS/IIS→isolated SQL at prior SHA; **not** Desktop→net10 Linux Host release E2E.

## Implemented in the VPS-09 source increment (pending independent PR review)

- Machine-readable 14-gate registry in `docs/testing/vps-09-gates.json` including 57 routes / 293 frozen case-pairs / 12 independent SQL Oracle scenario families / required exact-SHA workflow list.
- `scripts/vps09_verify.py` signed ED25519 SHA/file/case/gate/Owner-assertion verifier; source inventory mode (no SQL execution).
- `scripts/tests/test_vps09_verify.py`: synthetic positive and negative admission checks; results must come from new final-HEAD CI, not from this prose.
- `scripts/Invoke-Vps09WindowsAcceptance.ps1`: opt-in Windows x86 net48 publish + bounded 4-case deployed remote HTTPS→SQL suite, with strict TLS, opt-in, SHA, SDK and TRX verification. **Does not run attended GUI**, nor attest the Linux process binary or 57/293 SQL live matrix.
- `.github/workflows/vps-09-evidence.yml`: mandatory source gate on PR/main, protected manual Windows SQL and separately reviewed signed packet gate. No automatic staging or production rollout.

## Real acceptance ledger (must remain NOT VERIFIED until actual proof)

| Gate | State | Missing objective evidence |
| --- | --- | --- |
| VPS09-01 exact Windows+Linux candidate SHA | NOT VERIFIED | Same reviewed main SHA, published PE hash, Linux active process/release manifest on real machine |
| VPS09-02 57/293 protected real SQL observations | NOT RUN | Exact complete live route/case expected/actual/status/diff, not frozen SQL-free pairs |
| VPS09-03 WEB5 terminal operator | NOT RUN | Approved/rejected/not-observed, resume, permissions, persistent reconciliation |
| VPS09-04 WEB6 protected audit | NOT RUN | Enabled positive/negative, durable audit, fail closed/restart |
| VPS09-05 JWT/session | NOT RUN IN INTEGRATED STAGE | Real public TLS login/refresh/replay/revoke across restart |
| VPS09-06 permission/negative | NOT RUN IN INTEGRATED STAGE | Denied actor, security administration and error/timeout semantics |
| VPS09-07 idempotency/race | NOT RUN IN INTEGRATED STAGE | Same-key/different-body, lost ACK, oversell and transaction isolation |
| VPS09-08 ledger/multiwarehouse/COGS | NOT RUN IN INTEGRATED STAGE | Independent read-only SQL Oracle, 4-decimal and moving average |
| VPS09-09 schema migration/restore | NOT RUN IN PROTECTED STAGE | Fresh and accepted 9..13 lineage, journal bridge and backups |
| VPS09-10 faults/rollback/reconcile | NOT RUN | Real fault/retry/rollback and SQL/audit observed diffs |
| VPS09-11 real Linux VPS-08 stage | NOT VERIFIED | Approved Environment, actual systemd/Nginx/SQL TLS, reboot/recovery |
| VPS09-12 attended GUI | NOT RUN | Published Windows x86, native attended 100%/125% checkpoints |
| VPS09-13 cleanup | NOT RUN | Resource ownership, absence proof and preserved audit |
| VPS09-14 final-HEAD evidence/Owner release | NOT VERIFIED | Independent review, required workflows final SHA, external Owner approval and signature |

**Disposition:** RELEASE BLOCKED / NOT PRODUCTION READY. Do not close Issue #12 or claim complete while any gate above is NOT RUN, NOT VERIFIED, FAILED or SKIPPED. Follow [VPS-09 runbook](../runbooks/vps-09-integrated-acceptance.md) for environment admission, evidence schema, signer trust and operator procedure.

No real SQL instance, protected staging runner, attended Windows GUI, live 293-case SQL response trace or Owner production approval was available to the implementation session. Source/test CI on the PR, if successful, verifies only the tools. A distinct external release decision remains mandatory.
