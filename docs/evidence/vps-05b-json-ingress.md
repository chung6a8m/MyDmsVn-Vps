# VPS-05B — net10 STJ ingress, operation projections and Application parser ledger

**Repository** `chung6a8m/MyDmsVn-Vps` · **Issue** [#15](https://github.com/chung6a8m/MyDmsVn-Vps/issues/15) · **draft PR** [#16](https://github.com/chung6a8m/MyDmsVn-Vps/pull/16)
**Baseline** `main` at `2a629ca76d9330cc66fac251dabee6b084b58410` (VPS-05A merged).
**State**: IMPLEMENTATION / VALIDATION IN PROGRESS — **NOT ACCEPTED, NOT MERGED, NOT VPS-05 COMPLETE**.

## Changed boundaries

1. Net10 `VPS05_STJ_INGRESS` links the same route/controller files against `WireObject`, `WireArray`, `WireValue` and `WireToken` backed by System.Text.Json.Nodes. Legacy `net48` links directly to the original Newtonsoft APIs. Strict grammar, duplicate member rejection and bounded UTF-8/depth remain active in net10 `StrictWireJson`.
2. Net10 security-sensitive JWT header/payload, auth login and signed WEB6 receipt reader parse via strict STJ. Their admission, signature, issuer/audience and session checks remain in their original code paths. Do not confuse this source conversion with new runtime security signoff.
3. WEB2–WEB6 ingress, WEB4 request canonicalization and post-commit result codec, WEB5 status resolution and WEB6 response projections use STJ-backed native nodes. Preserve explicit null versus absence, ordered projection fields, canonical amount strings, `long` string IDs and immutable original request bytes. The six-kind independent WEB4 SHA-256 corpus is a hard gate.
4. Net10 `SalesReceiptCursor` uses Utf8JsonWriter/JsonSerializer and the same six-field cursor format and SHA-256 filter hash. Net10 permission import uses strict JsonDocument and unchanged schema/authority validation. Net48 compiler branch retains Json.NET.
5. `Net10WireJson` no longer introspects Newtonsoft JsonConverter attributes; explicit net10 sale/receipt calendar metadata maps date-only fields. Shared/desktop `net48` DTO annotations remain unchanged. Native response projections bypass the former Newtonsoft-JToken-to-JsonDocument adapter.

## Test obligations

| Gate | Current status | Exit proof |
| --- | --- | --- |
| Linux SDK 10.0.404 net10 locked build, warnings as errors | Checkpoint PASS at `29cd8e6` ([CI #44134384069](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44134384069)); **final-HEAD NOT YET VERIFIED** | Full exact-HEAD run and test counts |
| Native strict grammar, duplicate/unknown, unicode/depth/lexeme, explicit null | Tests added in PR #16; pending final-HEAD run | Native host tests + negative matrix |
| Frozen WEB4 six-kind canonical SHA256 and immutable source input | Tests retained and expanded; pending final-HEAD run | Independent `fingerprint-vectors.json` expected/actual/diff, no rehash of journal |
| Sales cursor opaque bytes and filter hash / permission import | Tests added + existing 366 Application regressions; pending final-HEAD run | Native and Application tests, zero skips |
| Actual Linux HTTPS→disposable SQL13, WEB2–WEB6 and WEB6 audit/signature/restart | **NOT VERIFIED ON FINAL HEAD** | CI job, SQL Oracle, all required groups and zero skips |
| Windows net48 build/discovery/published Desktop | **NOT VERIFIED ON FINAL HEAD** | Three Windows workflow conclusions and SHA |
| Independent review, remediation, final-HEAD CI | **OPEN** | Review findings and CI on same final SHA |
| Net10 `.deps.json` zero Newtonsoft | **NOT YET SATISFIED, scheduled VPS-05C** | Produced runtime file and strict negative inventory |
| Protected VPS staging and release integrated acceptance | **NOT VERIFIED (VPS-08/09)** | Separate Owner gates |

**Documentation discipline**: historical CI URLs apply only to their specific SHA. Do not promote a partial or cancelled run, SQL-free compile, route inventory, or disposable SQL success into full VPS-05 acceptance or production readiness. Source of truth for live status is [roadmap README](../roadmap/README.md).
