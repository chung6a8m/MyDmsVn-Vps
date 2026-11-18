# OWN-VPS-04 — Single-Owner VPS-08/VPS-09 closeout exception

- **Status:** APPROVED
- **Decision date:** 2026-11-19
- **Chosen option:** Single-Owner exception with compensating technical controls
- **Owner authority:** direct Owner choice in the Issue #12 closeout task

## Decision

For the single release candidate used to close Issues #11 and #12, Owner
`chung6a8m` may perform the VPS08-01..08 and VPS09-01..14 operator, evidence
review and release-approval duties without a second human reviewer. Evidence
created under this decision must state `NO_INDEPENDENT_HUMAN_REVIEW`; separate
accounts controlled by the same person must not be represented as independent.

This exception replaces only the human-separation requirements for that
candidate. It does not weaken or waive any runtime, SQL, GUI, recovery,
cleanup, exact-SHA, CI or signature requirement.

## Compensating controls

- Use two distinct, hash-bound machines/runners for first-pass execution and
  second-pass verification, each from a clean exact-SHA checkout.
- Pin the Owner Ed25519 public-key SHA-256 outside the evidence packet before
  protected acceptance starts; never store the private key in Git or GitHub.
- Enforce at least 24 hours between evidence finalization and the dated Owner
  `APPROVED_FOR_PRODUCTION` assertion.
- Keep exact-main Environment admission, disabled admin bypass, dedicated
  runner labels, immutable artifact hashes and fail-closed packet verification.
- Require all 14 gates and all 11 exact-SHA workflows to PASS with zero failed
  or required skipped cases. A source change invalidates the candidate packet.

## Scope and expiry

The exception applies only to VPS08-01..08 when executed as prerequisites of
VPS-09, and VPS09-01..14, for the Issue #12 closeout candidate. It expires when
that candidate is approved or rejected. It does not authorize production SQL,
production rollout, future releases, retroactive evidence or skipped gates.

## Explicit assurance limitation

Cryptographic signatures, runner separation and delayed second-pass review
reduce accidental and tampering risk but do not replace independent human
judgment. The evidence ledger, roadmap and issue closeout must preserve this
limitation verbatim and must not claim independent review.
