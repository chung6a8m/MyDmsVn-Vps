# VPS-04B — portable protected audit and Linux HTTP/security evidence

**Repository:** `chung6a8m/MyDmsVn-Vps`. **Increment:** Issue #10 / [PR #13](https://github.com/chung6a8m/MyDmsVn-Vps/pull/13) (draft at documentation authoring). **Base main:** `b53e5eeaffc4e3acafcda6c89f499894a66d4140` (VPS-04A PR #9 merged). **Verified code HEAD:** `b41eb7c62e5c9ffe37b634ab543f64b86c0bd779`. **Status:** IMPLEMENTED / CODE-HEAD CI VERIFIED; **not yet** independently reviewed, final-documentation-HEAD verified or merged. No production approval.

## Scope and boundaries

- Portable Linux-only audit sink, restricted to pre-provisioned canonical file inside candidate root: actual process owner, directory `0700`, regular single-link file `0600`, symlinks denied by `openat(O_NOFOLLOW)` on every path component, opened inode/mode validated using `statx`, cross-process advisory `flock`, append + disk `fsync`. Does not create, truncate or repair the destination; startup refusal and write errors sanitize paths/inner exceptions. Windows ACL sink remains in the legacy net48 project.
- `CoreApiAdmissionMiddleware` in native ASP.NET Core request scope. Explicit scope validation in all environments, existing HTTPS/origin/CSRF/correlation/size/Bearer and SQL session authorization boundary retained. Audit-readiness degradation maps health to sanitized `503 SecurityAudit.Unavailable`.
- WEB6 enabled only with a configured durable audit file; disabled mode retains prior trace sink behavior. Legacy Newtonsoft/MediatR/DbUp and 57-route native registration remain unchanged; source-level compile compatibility facades are **not** runtime OWIN/System.Web dependencies.
- Tests are real Ubuntu GitHub-hosted SDK 10.0.404 execution; SQL workflow provisions disposable SQL Server 2022 using verified localhost TLS, original DbUp 001..013, restricted runtime principal, independent SELECT-only nine-table oracle, seeded nonproduction actors. No real production, external SQL, VPS or protected staging is touched.

## Verified code-HEAD CI, 2026-11-17

| Check | Exact GitHub Action evidence | Result |
| --- | --- | --- |
| Linux .NET10 locked restore, Release build | [#44111184190](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44111184190) | PASS |
| Server.Application xUnit net10 | same run | 366/366 passed, zero failed/skipped |
| Native host, boundary, corpus, portable audit xUnit net10 | same run | 216/216 passed, zero failed/skipped |
| Frozen route signatures | same run | 57/57 passed |
| Disposable SQL13 Kestrel HTTPS baseline + SQL oracle | [#44111184115](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44111184115) | 15/15 passed, 0 failed/skipped; 24 source-bounded WEB2–WEB6 projections, WEB6 disabled for historical phase |
| **New enabled WEB6 portable protected audit** | same SQL run, three separately recorded TRX | **3/3 passed, 0 failed/skipped** |
| Enabled WEB6 specifics | same run | login; WEB6 context; valid original-journal bound receipt; current-state committed-result DENY; tamper rejection; anonymous rejection; SQL session logout/revocation; allowlisted durable audit and no token/password fields |
| Linux audit refusal/recovery | same run | privileged file drift: login refused, health 503; insecure mode startup rejected; correction + new Kestrel process recovers prior durable records |
| Windows Desktop publish | [#44111184124](https://github.com/chung6a8m/MyDmsVn-Vps/actions/runs/44111184124) | PASS at code HEAD; does not prove attended GUI |

Executed via GitHub Actions on exact code SHA; the PR contains later documentation changes, therefore **the table above is not a final-HEAD CI pass**.

## Exact reproducible gate

Linux SQL-free CI workflow: `.github/workflows/vps-04a-linux.yml`. Run from `src/MyDmsVn.Server.AspNetCoreHost`, SDK 10.0.404:

```bash
dotnet restore MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true --locked-mode
dotnet build MyDmsVn.Server.AspNetCoreHost.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore -warnaserror
dotnet restore ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true --locked-mode
dotnet test ../../tests/MyDmsVn.Server.AspNetCoreHost.Tests/MyDmsVn.Server.AspNetCoreHost.Tests.csproj -p:Vps04Server=true -c Release -f net10.0 --no-restore
```

Live SQL: `.github/workflows/vps-04a-ci-sql.yml` provisions a **strictly ephemeral localhost-only SQL2022 container**; it first runs its original 15-fact disabled-WEB6 evidence, then restarts with protected WEB6 mode and runs three new `Vps04bWeb6LiveSqlTests` facts, requiring exactly three single-executed passed TRX, mode refusal and worker restart. Do not rerun these live tests against external/production SQL; the bootstrap refuses contexts outside the approved ephemeral CI.

## Required separate reviews / remaining limitations

- Independent PR security/code review, findings remediation and CI on **final documentation+code HEAD** still required before merge and VPS-04B COMPLETE.
- Systemd, trusted proxy/TLS termination, VPS secrets management, protected independently reviewed staging and production-grade operations belong to VPS-08, **NOT RUN**.
- Exhaustive 57-route dynamic success/error matrix, Windows Desktop → Linux Host → SQL release workflows, and Owner release authorization belong to VPS-09, **NOT VERIFIED**.
- Filesystem permissions and durable append protect against untrusted local accounts, but do **not** claim a tamper-proof cryptographic ledger against process-owner or root compromise. Audit rotation and secure backup must be governed by VPS-08 operations.
