# VPS-10.7 — Native-only Host and paired handoff evidence

**Scope:** source implementation, not acceptance. Base main `922bdc8e7fda24cd53570cccbac7809d305088ed` (PR #44). Source/test implementation commit `612054995f9644ff72a3b63858a9d72f0ba72856`; the documentation commit and any follow-up fixes must be included in the final reviewed PR HEAD. CI evidence must name that final HEAD; baseline CI is not transferred.

## Changes

- 57-route native MVC inventory checked against compatibility identity; 404/405 JSON response handling remains outside MVC but uses the native typed envelope. No reflection dispatch. `--list-routes` retains SQL-free invocation and frozen identity fields.
- Remove production CompatWebApi, WireDom, Net10RouteTable, Net10WireJson and the Legacy wildcard source input. Move still-used config/auth/audit helpers to native-owned directories. JWT and signed WEB6 claim readers use bounded STJ without JToken aliases. No signing key/purpose, cookie, error semantics, route, operation fingerprint or stored outcome contract change intended.
- Preserve standalone net48 HttpHost and historical oracle sources. Historical VPS-05 DOM, writer and nine linked parser/projection/codec files compile only in the net10 test assembly. A test-only narrow query adapter supports the historical source parser; it is not a fake OWIN namespace or a production service. Frozen corpus, SQL schema, dependency pins, and Desktop source are unchanged.
- New source/runtime gate checks evaluated MSBuild Compile inputs and both build/publish runtime graphs; six Python negative-control tests. New Vps10BridgeRemovalTests suite: 11 cases (compiled type/reference boundary, native error bytes and metadata, valid and seven malformed signed bearer cases, signed WEB6 issue/validate and ambiguity rejection). Existing OpenAPI test asserts all 57 operations and their 200 schemas in addition to the existing path and schema assertions. Existing 293 differential cases and other exact-count gates are retained.

## Gate receipt (pending)

| Gate | State | Required evidence |
| --- | --- | --- |
| G0 static baseline | NOT RUN locally | Foundation, frozen inventory, route parity scripts; source-boundary negative controls |
| G1 Host/Application | NOT RUN locally | SDK 10.0.404 locked restore/build warnings-as-errors; focused 11/11 and whole-suite counts; evaluated compile inventory; actual 57/0 and development OpenAPI |
| G2 frozen differential | NOT RUN locally | Existing 293/293 native HTTP differential, no skip |
| G3 Desktop net48 | NOT RUN locally | Windows locked restore/build and discovery/execution of regression gates; exact x86 artifact |
| G4 SQL effects/recovery | NOT RUN locally | Disposable SQL existing workflow and side-effect/recovery oracles; no waived gate |
| G5 paired handoff | NOT VERIFIED | Published dependency graph, exact Desktop/Host pair and attended UI flows from the local simulation runbook, pending operation upgrade U01–U06 |

The available implementation environment provided repository read/write but no runnable full checkout/.NET SDK, Windows Desktop or paired SQL session. GitHub Actions results and independent review must be attached to the PR after final-HEAD execution. Do not infer final-HEAD PASS from the predecessor. No production readiness or release approval is claimed.

## Open items

- [Issue #45](https://github.com/chung6a8m/MyDmsVn-Vps/issues/45): physical pending-operation upgrade U01–U06 remains open; source-only acceptance in PR #44 applied to that PR only.
- Independent review, final-HEAD CI and exact paired artifact/attended observations still required. Do not close VPS-10 or claim full acceptance solely from source-only evidence.
- Operator steps: [Local full-stack simulation](../runbooks/local-fullstack-simulation.md); status authority: [Roadmap](../roadmap/README.md).

## Independent review — 2026-11-26

Reviewed original PR HEAD `82029ec9ab3ab632111bb4e004e328d620451b3b`, base `922bdc8e7fda24cd53570cccbac7809d305088ed`. The original 40-file diff removes the bridge, preserves the actual 57-route owner/flag contract and isolates the old wire oracle. Reviewed native admission/routing, signed token/receipt validation, error responses, source and dependency boundaries and the frozen test paths. No additional concrete production-code defect established by this review.

- **F01 (G5 artifact evidence):** the Windows VPS-03 workflow previously published and audited the Desktop x86 output only in runner temp; it did not retain a downloadable artifact, exact source checkout or per-file hash manifest for a same-SHA pair with the Linux Host. Review fix `f7c40e6013894bfc5f560bc7b77b0967f4126f1e` pins the PR HEAD checkout, verifies it, creates and checks the Desktop SHA-256 payload manifest and uploads the audited artifact. This is artifact preparation only; execution and artifact evidence on the new final HEAD are required.
- Original HEAD CI: 11/11 workflow conclusions SUCCESS. Logs show Application 366/366, Host 1254/1254, bridge removal 11/11, frozen differential 293/293, compiled routes 57 native/0 bridge, isolated HTTPS/SQL 16/16 plus retained named WEB6/recovery/mutation gates, migration 58/58, net48 Desktop.Session 276/276 plus typed-wire 38/38 and legacy HttpHost 684/684 plus OWIN oracle 1/1. Linux published 61-file SHA-pinned artifact with 31 runtime libraries. These are **original-HEAD** results, not review-fix evidence. The normal PR build/test jobs that checked out synthetic merge commit `d5170e606f35a7aa19950ed19ff161deb63fa976` used the same Git tree `24d0b9011a2c24519758744c2c07e63d06f6e76e` as the original HEAD; exact-source Linux artifact jobs checked out the original HEAD itself.
- Original runs: [Linux native](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45796139999), [disposable SQL](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45796139852), [migration SQL](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45796139845), [Windows foundation](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45796139731), [Desktop publish](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45796139950), [Linux publish](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/45796139792).
- **Merge gate remains OPEN / NOT VERIFIED:** final review-fix CI and matched downloadable artifact manifests must be checked; attended paired Windows Desktop → Linux Host → isolated SQL observations and U01–U06 are not supplied. Protected SQL/staging/integrated-release jobs remain intentionally skipped on ordinary PRs, not PASS. PR #44's dated source-only exception is scoped to that candidate; do not extend it to this PR without a new explicit Owner decision. No deployment/release/production acceptance.

Final-HEAD run receipts and merge decision belong in PR #47 after this documentation-inclusive commit. The live roadmap remains IN PROGRESS.
