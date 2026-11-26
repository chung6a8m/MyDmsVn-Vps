# VPS-10.3 — Desktop typed HTTP wire (source review candidate)

Base main: `6c77d474b79095673bd0881eaee7e9c64ad2981b` (PR #35 merged). OWN-VPS-05 remains controlling. Desktop net48/Json.NET and Host net10.0/STJ stay unchanged as runtime targets; route ownership remains 4 native / 53 bridge. No Server, SQL schema, dependency, UI culture policy or persisted pending record format change.

## Implementation boundary

- Adapter-only Sales and Receipt typed mutation DTOs: exact field names and ordering, invariant decimal/Int64 strings, calendar date, explicit null note, expectedVersion included only for updates/posts; Security revisions stay numeric.
- Existing WEB4-v1 canonicalizer remains unchanged after conversion of typed source to a DOM at the canonicalization boundary. Legacy request hash and dispatch use the same serializer settings.
- Explicit receiptId/receiptLineId to neutral Id mapping; typed receipt page mapping. Sales response contracts already use the correct saleId/saleLineId names and remain shared unchanged. One adapter codec for live and retained journal outcomes; no receipt rename / serialize / parse or journal outcome JSON text roundtrip. The journal identity, target check, durable fence and no-resend behavior remain unchanged.
- 16 targeted Windows cases: four cultures each for six independent fingerprint corpus vectors and exact request bytes, live/retained Sales and Receipt reads; four malformed receipt envelope cases; two scalar/presence/error tests; two historical receipt recovery cases (matched/mismatched target). Existing adapter/pending regression suite and native Host frozen oracle remain required. Windows discovery job also executes the two focused numeric/date selectors separately, including the partial GoodsReceiptControlTests methods; each must have nonempty, all-passed TRX. This is not attended GUI acceptance.

## Verification

Source prepared via GitHub API; local container has neither .NET SDK nor Windows and direct Git remote access is unavailable. Local restore/build/test: NOT RUN. No fabricated test count. CI must run on the exact final HEAD; the PR links provide the source SHA and run records. Required G3: Windows SDK 8.0.400 locked restore, Release Desktop net48 build, focused 16/16 typed/replay cases, full Desktop.Session regressions, focused ReceiptNumericInputTests and DesktopDateInputIntegrationTests (including relevant partial GoodsReceiptControlTests methods), Windows artifact/discovery. G1/G2 and existing required CI workflows must remain green; no change to the frozen 57-route/293-case differential or its expected values. Verify actual test counts/zero required skips and link exact-SHA run receipts before completion. Paired Windows/Desktop and native Host artifact/runtime evidence is separate from attended physical acceptance; no deployment or production acceptance is granted.

Review/merge and any additional fixes are separate gates. No VPS-08/VPS-09 operational status changes.
