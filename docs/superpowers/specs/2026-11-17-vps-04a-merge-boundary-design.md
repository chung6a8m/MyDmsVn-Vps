# VPS-04A merge-boundary design

## Intent

Make PR #9 reviewable as the VPS-04A increment without claiming that protected staging, portable WEB6 audit, VPS deployment, or integrated release acceptance has already happened. The roadmap remains the only live status authority.

## Goals

- Verify the R17 test hardening already present on the PR HEAD.
- Define the VPS-04A merge gate from the approved roadmap: Linux SDK 10.0.404 restore/build, the separate net10 Server graph, ASP.NET Core host/core API parity, nonempty tests, and exact-HEAD CI evidence.
- Move later work into linked, scoped follow-up issues for VPS-04B, VPS-08, and VPS-09.
- Update PR #9 from Draft to Ready for review only after final documentary-HEAD CI passes.

## Non-goals

- No production runtime, schema, grant, frozen fixture, package, or serializer change.
- No protected Environment label, secret, SQL mutation, deployment, merge, release approval, or production-readiness claim.
- No claim that 178 source files are 178 live HTTP cases or that bounded route projections are exhaustive response parity.

## Constraints

- Preserve all repository invariants and the VPS-02 → VPS-03 → VPS-04 sequencing.
- `docs/roadmap/README.md` remains the sole live status authority.
- Required real environments stay `NOT RUN` unless actual evidence exists.
- A skipped protected staging job is never counted as PASS.

## Approaches considered

1. **Close VPS-04A narrowly and link future increments (selected).** Keeps PR #9 reviewable and aligns each outstanding gate with the roadmap increment that owns it.
2. Continue expanding PR #9 through all 57-route variants and reliability matrices. Rejected because it conflates VPS-04A, VPS-04B, and VPS-09 and makes the PR unbounded.
3. Require protected staging before VPS-04A review. Rejected as a VPS-08/09 environment gate; it requires external administration and still would not prove portable audit or full integrated acceptance.

## Acceptance

- R17 focused and complete Application suites pass with zero required skips.
- Three non-duplicative follow-up issues exist and are linked from roadmap/evidence/PR.
- Roadmap distinguishes VPS-04A review/merge readiness from VPS-04B, VPS-08, and VPS-09 work.
- All PR workflows finish on the exact final documentation HEAD; staging remains explicitly `SKIPPED / NOT RUN` unless separately approved.
- PR #9 is Ready for review, mergeable, and explicitly not merge-approved until independent review occurs.
