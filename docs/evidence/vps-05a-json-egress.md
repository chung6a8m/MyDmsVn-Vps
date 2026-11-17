# VPS-05A — System.Text.Json net10 egress checkpoint

**Repository:** `chung6a8m/MyDmsVn-Vps` · **PR:** [#14](https://github.com/chung6a8m/MyDmsVn-Vps/pull/14) · **base main:** `61e8a2126c8fe9844fd3aefb6724515e1dd80203` (PR #13 merged) · **review state:** DRAFT; code checkpoint `e0fc2dbef8f010dce2bc58246eb070fe2d5ee2e5` 5/5 workflows PASS. Final documentary-HEAD run after this evidence update remains a distinct required check.

## Implemented (bounded)

1. ASP.NET Core `net10.0` host excludes the legacy `HttpJson.cs` source in its own project file and defines `VPS05_STJ_EGRESS`. The standalone `net48` HttpHost project continues compiling the original `HttpJson.cs`, untouched.
2. New `Net10WireJson` emits an explicit single-member `data` or `error` envelope using `System.Text.Json`, with HTTP status separate, camelCase properties, null omission and invariant numeric/date formatting. It temporarily converts existing **trusted** Newtonsoft `JToken` projections through `JsonDocument` to retain explicit WEB3 nulls and stable wire names.
3. Linked `WebOperationResultCodec.Encode` uses the **same** new writer only in the net10 host. Historical net48 codec path and immutable WEB4 canonical-input fingerprint algorithm remain unchanged.
4. Added six differential xUnit v3 tests: frozen success and error envelope strings; error detail/null omission; decimal four-place and UTC fidelity under `vi-VN`; WEB3 explicit null, int64 string and property ordering; and a generated WEB3 receipt journal result compared to HTTP writer plus strict codec decode.

## Verification policy and current evidence

| Gate | Status | Evidence to record |
| --- | --- | --- |
| Source changes, independent repository scope | IMPLEMENTED at initial `ac18a62dc568bc193fa07b0eb171d41ff5996096` | PR #14 diff; no prior PASS is inherited |
| Linux SDK 10.0.404 compile, locked restore and `--warnaserror` | **PASS at checkpoint `e0fc2db`** | [Linux net10 #44123450174](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44123450174), all required job steps successful |
| Six new xUnit tests, original 366 Application / existing native corpus and fingerprint tests | **PASS at checkpoint `e0fc2db`** | [Linux net10 #44123450174](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44123450174): Application **366/366**, native HTTP **225/225** (includes six new parity tests), 0 failed, 0 skipped |
| 57 native routes and 24 bounded WEB2–6 HTTPS→SQL projections | **PASS bounded disposable CI at `e0fc2db`** | [Linux SQL2022 #44123450126](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44123450126): 57/57 routes; DbUp SQL13 13/13; **15/15** HTTPS→SQL tests; **24/24** scoped expected/actual/diff; additional **3/3** WEB6 enabled live audit tests, zero fail/skip. WEB6 was disabled during its historical 24-case phase. Not staging or exhaustive |
| Windows `net48` HttpHost/Desktop and xUnit foundation | **PASS at checkpoint `e0fc2db`** | [Windows net48 #44123450744](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44123450744) 8/8 jobs; [xUnit discovery #44123450609](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44123450609) 10/10 jobs; [Desktop publish #44123450301](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44123450301) PASS |
| Native `net10` runtime graph with **zero Newtonsoft** | **NOT SATISFIED (by design)** | later VPS-05B+ refactor and produced `.deps.json` gate |
| All 57 dynamic route/value/negative variants, protected staging, attended GUI | NOT VERIFIED | VPS-05B / VPS-09 / VPS-08 as applicable |

**CI audit rule:** do not fill in PASS, counts, CI URLs or SHA until a completed run on the exact final PR HEAD has been inspected. A fresh PR run before further fixes is historical evidence only.

## Deferred VPS-05 acceptance blockers

- Replace net10 `StrictWireJson`, `StrictJsonRequest`, WEB2–WEB6 `JObject/JToken` decoders/output projections with `JsonDocument` / `JsonElement` / typed DTOs, while preserving strict duplicate/unknown property refusal, UTF-8/Unicode and query/path grammar.
- Move net10 DTO annotations and date converters off Newtonsoft without changing Desktop `net48` serialization; migrate `PermissionAssignmentImportParser` and `SalesReceiptCursor` with golden byte-level cursor/fingerprint oracles.
- Prove `decimal` lexical scale, `long` beyond IEEE-754 safe integer, calendar/UTC/date-offset constraints, null vs absent, error/status/cookie/auth/session, idempotency six-kind canonical inputs and preserved journal bytes across serializer generations.
- Remove Newtonsoft package and `Json.NET` runtime references from net10 **produced** `.deps.json`; leave Windows Desktop's Newtonsoft intact.
- Independent review, final-HEAD Linux/Windows CI and fresh HTTPS→SQL13 evidence. VPS-08 protected VPS staging and VPS-09 release acceptance are separate and cannot be inferred.

## Commands expected on equipped runner

```bash
cd src/MyDmsVn.Server.AspNetCoreHost
dotnet restore MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true --locked-mode
dotnet build MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore -warnaserror
dotnet test ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore --logger trx
```

Current execution environment has no `dotnet` executable; CI runs in GitHub-hosted Linux/Windows. The five URLs above are **code-checkpoint** evidence at `e0fc2db`; do not transpose them to a later documentation or review-fix HEAD.
