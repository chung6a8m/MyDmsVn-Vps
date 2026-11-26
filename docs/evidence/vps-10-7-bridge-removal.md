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
