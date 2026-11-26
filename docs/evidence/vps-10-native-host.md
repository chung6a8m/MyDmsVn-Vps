# VPS-10.0 — Native host contract baseline and characterization

## Scope and evidence boundary

Owner decision: OWN-VPS-05. Base / paired Desktop and Host source: `58111f95f8dbb708fbd7c9ce8480b3f952dbaad3` (main, 2026-11-26). This PR changes test code, CI and documentation only; zero native route migrations, zero production/package/SQL schema changes. The existing net48 client and net10 host are unchanged. No deployment/release approval. The authoritative status is [roadmap index](../roadmap/README.md); final commit SHA, CI and independent review must be recorded in this implementation PR.

## Before changes — exact baseline CI

All ten workflows returned success on the pinned main push; protected jobs remain skipped, not passed. The G0/G1/G2 commands were run by existing hosted CI (not locally); Linux Ubuntu 24.04, SDK 10.0.404, locked restore/build and TRX no-failure/no-skip assertions. Windows-2022 uses the existing net48 SDK 8.0.400. Actual baseline logs:

| Gate | Actual result | Exact baseline run |
| --- | --- | --- |
| G0 | Static foundation and VPS-01 topology; 29+28 route source signatures; historical parity ledger 57/57 | [foundation](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45748922054), [Linux](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45748922121) |
| G1 | Application 366/366, Host 699/699, route inventory 57/57, zero failed/skipped; runtime boundary PASS | [Linux job](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45748922121/job/139671707748) |
| G2 | 293/293 independent net48 HTTP observations, zero failed/skipped | [Linux job](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45748922121/job/139671707748) |
| Desktop baseline | 238/238 session tests, zero failed/skipped; existing Windows builds/discovery | [foundation job](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45748922054/job/139671707699), [discovery](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45748922010) |
| Disposable SQL | 16/16 main HTTPS-to-SQL13 facts (11 oracle + 5 grouped WEB2–WEB6), additional 3+1+1+1 selectors passed; zero failed/skipped in these selectors | [SQL2022 TLS job](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45748922016/job/139671707489) |
| Protected real SQL / attended GUI / release | NOT RUN / NOT VERIFIED for VPS-10.0 | `approved-linux-sql` skipped; no change to VPS-08/09 |

Source review and manifest construction use the pinned GitHub tree. No full checkout or local SDK is available in the editing environment; local runtime test execution NOT RUN. The existing Linux workflow has a separate exact-count targeted selector for the new 37 characterization cases plus a frozen-source Git blob pin check; existing full-suite and 293-case differential gates stay unchanged. Execute G0/G1/G2 on the new PR head; no new-head PASS is asserted by this commit.

## Manifest schema and coverage

[Version 1 manifest](../contracts/vps-10-route-migration.json): `id` = verb + space + exact route template, `order` = baseline attribute Route.Order, baseline controller/action and `activeOwner=bridge`; `owningIncrement` is the future migration owner (not a second status ledger); `contractFamily`, authentication, feature flags, and required positive/negative variant IDs. `coverage.rawHttpCaseId` and `rawHttpSamplePath` map to the existing SQL-free live net48 oracle. Source pins are Git blob SHAs; no oracle bytes regenerated. The mapping uses the actual 57 representative case IDs/methods/paths in VPS-05C, matches the frozen route inventories and retains the 46 remote Desktop methods. 52 v1 + 2 v2 + 3 v3.

| Increment | Routes | Main characterization obligations |
| --- | ---: | --- |
| 10.2 lookup | 4 | raw query, empty GET body, enabled with Desktop API disabled, permissions |
| 10.4A Sales reads | 2 | page/cursor/date/ID, response scale, null omission |
| 10.4B Sales writes | 3 | strict presence and types, version grammar, operation fingerprint, rollback/replay |
| 10.5A Receipt/Inventory reads | 4 | pagination, UTC vs calendar, explicit null/4–8 place scale |
| 10.5B Receipt writes | 3 | strict version grammar, precision, stock/cost/replay |
| 10.6A Catalog CRUD | 20 | Desktop gate, binding/permissions, read/write concurrency |
| 10.6B Security/Foundation | 8 | numeric revision precision, permissions/audit, feature gate |
| 10.6C Auth/Health | 6 | cookies, refresh, challenge, readiness, transport admission |
| 10.6D Operations | 7 | order 0/1/2, WEB4/5/6 flags, stored outcomes and resend |

| Evidence dimension | Frozen / new baseline | Limit / future gate |
| --- | --- | --- |
| Raw HTTP | 57 case IDs, 293 observed variants; new Kestrel tests pin query decoding, body/content-type, fields, error envelope/precedence and feature gating | Existing live fixture uses in-process loopback and fake handlers; not physical SQL. HttpClient URI canonicalization is not a byte-transparent malformed-percent test; raw socket edge cases remain native pilot obligations. |
| Canonical bytes | Six operation route scopes linked to immutable WEB4 fingerprint vectors; exact UTF-8 bytes/hash under vi-VN/en-US/de-DE and source immutability | Native canonicalizer and pending-state/replay integration verified in 10.4B/10.5B/10.6D; old vectors never overwritten. |
| Desktop | 46 mapped remote methods; net48 regression/session and wire corpus in existing CI; routes outside method matrix are not asserted absent from Desktop | No new Desktop adapter or attended GUI/culture-input claim; G3 targeted culture and pending/replay checks in 10.3. |
| SQL effects | Per-route `historicalSqlLedger` preserves old 16 grouped reads / 10 prior bounded / 3 disabled / 28 route-only classification; current disposable SQL group run is separate and stronger but bounded | Do not infer per-route write side effects from SQL-free fixtures or historical ledger. G4 must prove each changed write/auth/recovery slice and its side-effect oracle. |

Bridge debt remains by design: legacy controller source links, reflection dispatch, HttpRequestMessage/HttpResponseMessage conversion, StrictWireJson/WireDom, and Desktop response DOM normalization. VPS-10.0 locks behavior before removing them. No assertion of improved speed.

## After changes / acceptance

- New `Vps10ContractCharacterizationTests`: 37 planned VSTest cases, manifest completeness and variant coverage, live raw HTTP binding/error tests and canonical culture vectors. The frozen 293-case selector remains separate.
- G0/G1/G2 final-head execution: pending GitHub Actions evidence. Review final HEAD and record counts/CI URLs before marking COMPLETE; if failures, repair tests against independent contract evidence rather than changing the API.
- G3/G4 regression workflows remain unchanged; production VPS-08/VPS-09 gates are explicitly out of scope.

