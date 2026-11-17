# VPS-05B — System.Text.Json ingress, operation projections and Application parser evidence

**Repository:** `chung6a8m/MyDmsVn-Vps` · **Issue:** [#15](https://github.com/chung6a8m/MyDmsVn-Vps/issues/15) · **Implementation PR:** [#16](https://github.com/chung6a8m/MyDmsVn-Vps/pull/16)
**Baseline:** `main` `2a629ca76d9330cc66fac251dabee6b084b58410` (VPS-05A merged).
**Status:** IMPLEMENTED / CI CODE CHECKPOINT PASS / INDEPENDENT REVIEW PENDING — **NOT MERGED, NOT VPS-05 ACCEPTED**.

## Verified implementation boundary

1. The `net10.0` HttpHost `VPS05_STJ_INGRESS` path uses a strict System.Text.Json-backed DOM (`WireObject`, `WireArray`, `WireValue`). `StrictWireJson` retains the frozen grammar and rejects duplicate properties, comments, trailing content, malformed Unicode, invalid depth/UTF-8 and untrusted numeric coercion. The same linked files compile with their **original Json.NET code on net48**.
2. WEB2 login/auth, Bearer JWT header/payload and signed WEB6 receipt readers use strict STJ parsing. Invalid-scalar claim casts are fail-closed, not HTTP 500. Session/issuer/audience/signature checks are still handled by the original authentication code paths.
3. WEB2–WEB6 request decoding, WEB4 operation canonicalization and journal codec, WEB5 resolution envelope, WEB6 eligibility/receipt projection and inventory DTO projections use native STJ nodes in the net10 host. Preserve immutable input, explicit null versus absence, ordered fields, four-decimal text, `9007199254740993` and `long.MaxValue` as decimal-string identifiers.
4. Net10 `SalesReceiptCursor` uses Utf8JsonWriter/JsonSerializer with unchanged cursor and filter-hash layout; `PermissionAssignmentImportParser` uses JsonDocument with strict duplicate/schema/permission gates. Net48 retains Json.NET implementations.
5. Net10 `Net10WireJson` no longer inspects Newtonsoft converter attributes or performs a Newtonsoft JToken-to-JSON response adapter. Explicit Server calendar metadata covers sale/receipt date-only fields; other timestamps retain UTC/Unspecified writer semantics. The shared Desktop/net48 contract annotations are untouched.
6. Added `Vps05bJsonIngressParityTests`; adapted frozen six-kind WEB4 and 05A differential oracles to compare STJ egress with the independent legacy writer.

## Exact checked code SHA and CI results

Code checkpoint: **`2e050dbba5834e7286598f9bcebc93221599a608`**. Each workflow below reported **SUCCESS on that SHA**, verified against the logs and job conclusions on 17 November 2026.

| Gate | Result on code checkpoint | Workflow evidence |
| --- | --- | --- |
| Ubuntu .NET SDK 10.0.404, warning-as-error build, provider-free tests, 57/57 route ledger | **PASS**; Server Application `366 passed / 0 failed / 0 skipped`; ASP.NET Core Host `248 passed / 0 failed / 0 skipped`. Route map **57/57**. The workflow's optional `approved-linux-sql` job was **skipped by design**; the independent dedicated SQL workflow below is required. | [Linux build and route gate #44135741940](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44135741940) |
| Linux HTTPS to isolated SQL Server 2022 with TLS, golden stock and WEB2–WEB6 SQL acceptance | **PASS**: `15/15` bounded HTTPS→SQL facts, `24` WEB2–WEB6 expected/actual/diff scenarios with WEB6 **disabled/fail-closed** in that matrix; separately `3/3` WEB6 **enabled** audit, refusal/degradation and restart/recovery HTTPS→SQL facts. None failed/skipped in the required subsets. | [Self-contained Linux SQL13 #44135741973](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44135741973) |
| Windows net48 legacy build/smoke and dependency guards | **PASS**, all `8/8` workflow jobs (including legacy HTTP host, Desktop and migration build). | [Legacy net48 foundation #44135741960](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44135741960) |
| Windows published Desktop artifact boundary | **PASS**, Windows net48 publish/dependency audit; **not** GUI/SQL E2E. | [Desktop artifact #44135741968](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44135741968) |
| Windows xUnit v3 compile/discovery | **PASS**, all `10/10` workflow jobs; discovery/static inventories do **not** imply SQL E2E for all Windows test projects. | [xUnit v3 discovery #44135742005](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44135742005) |

**Commit consistency:** this evidence update is a documentation-only commit following the above checked code SHA; the PR's final merged HEAD must independently rerun all five workflows and have the **same exact SHA across the final results**. Record final CI URLs in PR #16; do not treat the previously checked code checkpoint as final-head verification after this documentation commit.

## Explicitly not claimed

- Independent code/security/contract review of PR #16 has **not** been completed. No merge authorization has been given.
- VPS-05C: exhaustive response/headers differential across all 57 route variants and **zero Newtonsoft in the produced net10 runtime `.deps.json`** remain OPEN. The transitive/legacy dependency is intentionally not eliminated by 05B.
- VPS-08 protected real staging and VPS-09 integrated Owner release approval remain **NOT VERIFIED**. Disposable CI SQL is not production/PREPROD and route-map reachability is not exhaustive API coverage.
- Historical fixture files and test oracles were not rewritten to fit the new serializer; only test call sites now pass the STJ-native net10 DOM while the legacy fixtures remain untouched.
