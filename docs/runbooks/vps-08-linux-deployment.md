# VPS-08 — Linux VPS operator deployment and hardening

Scope: [Issue #11](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11), [PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27), Owner decisions OWN-VPS-01/02 and the candidate-scoped OWN-VPS-04 exception. This document is an **operator procedure**, not proof that a VPS or protected SQL environment exists. Live status belongs only to [roadmap README](../roadmap/README.md).

## Target and trust boundaries

Reference design: Nginx with validated HTTPS certificate at the public hostname; private ASP.NET Core net10 Kestrel only at 127.0.0.1:5080; systemd dedicated unprivileged identity mydmsvn; restricted SQL Server runtime principal over verified encrypted TLS. SQL Server need not be on the VPS. In VPS-08 reverse-proxy mode, the Host also validates that SqlClient is configured for mandatory/strict encrypted transport with server certificate verification; optional encryption or TrustServerCertificate=true refuses startup. Nginx overwrites X-Forwarded-For with the immediate client IP, sends exactly https as X-Forwarded-Proto, preserves the exact public Host, and supplies a private proxy token from a root-owned include. Kestrel rejects unauthenticated, unknown, chained and non-loopback forwarding before ASP.NET Core forwards only For and Proto from 127.0.0.1. Never set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, expose Kestrel publicly or use a wildcard known proxy.

Three secrets (restricted SQL string, JWT signing allowlist and proxy token) are loaded via systemd LoadCredential into the wrapper and exported to the child process. The wrapper must resolve each credential filename only after binding its `name` argument; VPS-08 source gates execute the real loader against isolated credential fixtures to catch Bash same-`local` expansion errors. The existing application reads them from environment: they remain accessible to root and the same service identity. Do not co-host untrusted code under that user. Nginx and Kestrel must use the identical independently generated token (at least 32 bounded ASCII characters), never committed to GitHub or printed in logs. Rotate it by a coordinated service+proxy change. The non-secret runtime.env must contain the exact HTTPS public origin and issuer, web5/exact13 mode, existing superuser binding and approved WEB6 protected candidate values.

## Protected staging prerequisites

1. Independently reviewed **isolated nonproduction SQL13**, compatible verified DbUp/FluentMigrator journal bridge and schema fingerprint, tested backups/restores, restricted runtime principal and a **separate read-only SQL Oracle**. All SQL connections verify a trusted CA and hostname with Encrypt=True and TrustServerCertificate=False. No production account, database or shared backup. The Host must never auto-migrate. Use [VPS-07B migration procedure](vps-07b-sql-migration.md).
2. Dedicated staging Linux machine with monitored disk, systemd, Nginx, ASP.NET Core Runtime 10, approved firewall and TLS certificate. Allow external 443 only, never internet 5080/SQL. Configure protected audit pre-provisioning from the [VPS-04B runbook](vps-04b-protected-audit.md): private candidate root 0700, regular file 0600 created only once with exclusive/no-follow flags and fsync. Do not truncate or independently rotate the audit file.
3. GitHub Environment named `vps08-sql13-nonproduction`, zero required reviewers under OWN-VPS-04, admin bypass disabled and only exact main allowed; secret `VPS08_STAGING_HOST_ATTESTATION`; variables `VPS08_STAGING_PROBE_URL`, `VPS08_STAGING_MACHINE_SHA256` (SHA-256 of the Owner-reviewed host `/etc/machine-id`; do not publish the raw identifier), `VPS08_RECEIPT_STATE_PATH`, `VPS08_OWNER=chung6a8m` and frozen `VPS09_CLOSEOUT_CANDIDATE_SHA`; and exactly one dedicated runner labeled `vps08-staging` operating as `vps08runner`. The receipt state path must be new for one candidate. No staging job runs on normal PR or push. Environment configuration is an external admin step, not established by CI.
4. Pin the full reviewed 40-character main SHA, reviewed artifact and complete same-SHA required CI. Confirm external Owner authorization to use staging SQL and ensure backup/restore and sensitive host cleanup plan are approved.

The staging operator pins the actual machine independently before enabling the protected Environment. Record only the hash (not raw machine-id) as the Environment variable `VPS08_STAGING_MACHINE_SHA256`:

    sha256sum /etc/machine-id

The Owner can configure and verify with locally authenticated `gh`:

    python3 scripts/configure_vps09_gates.py configure --environment vps08-sql13-nonproduction
    # Manually disable admin bypass and set environment secret/variable names
    python3 scripts/configure_vps09_gates.py verify --environment vps08-sql13-nonproduction --expected-sha EXACT_MAIN_SHA \
      --admin-bypass-attestation confirmed-disabled:OWN-VPS-04:vps08-sql13-nonproduction:EXACT_MAIN_SHA

The helper checks protection and names, **not secret values or SQL/VPS reality**. The admin-bypass argument is a logged Owner attestation because GitHub's Environment response does not expose that UI setting; it is not an API-detected fact. Remove `VPS09_CLOSEOUT_CANDIDATE_SHA` or disable the Environment immediately when the candidate is approved or rejected. The previous PR #9 SQL job skipped by design and all disposable GitHub Actions SQL containers are not staging acceptance.

## Install Linux host on a fresh isolated VPS

Use root/operator commands from a pinned, reviewed source checkout; do not fetch unreviewed main or alter an existing audit file:

    sudo useradd --system --no-create-home --shell /usr/sbin/nologin mydmsvn
    sudo install -d -o root -g root -m 0755 /opt/mydmsvn /opt/mydmsvn/releases /usr/local/libexec
    sudo install -d -o root -g root -m 0750 /etc/mydmsvn
    sudo install -d -o root -g root -m 0700 /etc/mydmsvn/credentials /etc/nginx/mydmsvn
    sudo install -d -o mydmsvn -g mydmsvn -m 0700 /var/lib/mydmsvn /var/lib/mydmsvn/audit
    sudo install -o root -g root -m 0755 deployment/linux/mydmsvn-vps-start /usr/local/libexec/mydmsvn-vps-start
    sudo install -o root -g root -m 0755 deployment/linux/vps08-rollout /usr/local/libexec/vps08-rollout
    sudo install -o root -g root -m 0755 scripts/vps08-manifest.py /usr/local/libexec/vps08-manifest.py
    sudo install -o root -g root -m 0644 deployment/linux/mydmsvn-vps.service /etc/systemd/system/mydmsvn-vps.service

Manually render deployment/linux/runtime.env.example with approved values, and install root-owned at /etc/mydmsvn/runtime.env. No placeholders may survive. Provision credential files /etc/mydmsvn/credentials/sql_connection_string, jwt_keys and proxy_token privately with root:root 0600. SQL credential must belong to **runtime**, never maintenance or Oracle. Render deployment/linux/mydmsvn-nginx.conf.tpl with approved literal domain, fullchain and private key path; install root-owned to /etc/nginx/sites-available/ and enable site. Provision /etc/nginx/mydmsvn/proxy-token.conf root:root 0600 containing one proxy_set_header X-MyDmsVn-Proxy-Token directive for the matching private token. Never send nginx -T output to CI or an Issue; it would reveal the include content. Use nginx -t, then reload; install cert renewal monitoring and a default vhost that rejects unexpected Host.

Run systemd-analyze verify against the installed service, systemctl daemon-reload and enable mydmsvn-vps.service. The service points to /opt/mydmsvn/current, which must be installed before first start. Its StateDirectory and protected audit path are the only intended write locations; audit record retention must not be delegated to ordinary logrotate.

## Source-pinned publish, rollout and rollback

The VPS-08 CI job restores with exact SDK 10.0.404/locked graph, runs 33 native proxy, SQL TLS and real SQL-free Kestrel forwarded-header/API boundary cases and publishes framework-dependent net10 with UseAppHost=false. The artifact has vps08-manifest.json including every file digest/size and full source SHA. This is an integrity manifest, **not a digital signature**. Operator must independently verify final reviewed GitHub Actions run, provenance and every gate before importing the package onto staging.

    python3 scripts/vps08-manifest.py verify /trusted/package EXACT_40_HEX_SHA
    sudo /usr/local/libexec/vps08-rollout deploy /trusted/package EXACT_40_HEX_SHA https://staging.example/api/v1/health
    sudo systemctl is-active mydmsvn-vps.service
    curl --fail --silent --show-error --proto '=https' --tlsv1.2 https://staging.example/api/v1/health

The root-owned rollout helper refuses symlinks, group/other-writable artifacts or paths, special file modes, concurrent deploy/rollback (exclusive flock), and mutated package files. An already-installed release of the same SHA may be restarted only if its manifest is byte-identical to the verified newly downloaded package and all installed entries remain root-owned; it is never overwritten in place. A release left partially copied by an interrupted initial deployment cannot be reused: the manifest or ownership admission must fail; clean it up only under root/operator change control after preserving failure evidence. `flock` (util-linux) must be installed on the VPS. A deployment health check requires both validated public HTTPS and an active systemd MainPID whose `/proc/PID/cwd` resolves to the exact immutable release SHA selected for deploy/rollback. The `systemctl restart` operation itself must succeed; neither a same-SHA retry nor an automatic recovery may declare success solely from a previously healthy process when restart fails. A stale service from the prior release must never certify the new release, even when Nginx and the health endpoint are green. It installs to /opt/mydmsvn/releases/FULL_SHA, atomically changes current, restarts systemd and polls publicly validated TLS health. On failed health it attempts restoring the previously manifest-verified binary and **returns failure even if recovery succeeds**. Rollback never downgrades SQL or undoes transactions. Only use an old binary when SQL schema, auth sessions and wire compatibility are independently proven:

    sudo /usr/local/libexec/vps08-rollout rollback PREVIOUS_40_HEX_SHA https://staging.example/api/v1/health
    sudo /usr/local/libexec/vps08-rollout rollback CANDIDATE_40_HEX_SHA https://staging.example/api/v1/health

Verify a direct unauthenticated loopback HTTP request (Host matching staging domain) gets HTTP 400, while public HTTPS health succeeds. Wrong Host, tampered credential, chained/unknown forwarded headers must be refused. Preserve protected audit records and inspect systemd journal privately (never upload raw tokens, SQL strings or audit data). Monitor TLS expiration, process restarts, audit inode/disk capacity and DB reachability.

## Acceptance ledger: strict external evidence needed

| ID | Real staged test | Status absent actual protected VPS |
| --- | --- | --- |
| VPS08-01 | Reviewed SHA, OWN-VPS-04 zero-reviewer Environment, bypass disabled, exact main, dedicated runner | NOT VERIFIED |
| VPS08-02 | Isolated SQL13, fingerprints/journals, verified TLS, restricted Host user and separate read-only Oracle | NOT VERIFIED |
| VPS08-03 | Nginx TLS, proxy trust and negative Host/headers/direct HTTP | NOT VERIFIED |
| VPS08-04 | systemd identity, service credentials, WEB6 protected audit mode/fsync and fail closed | NOT VERIFIED |
| VPS08-05 | Actual service restart, SIGKILL/process-loss auto restart, health and SQL/audit reconciliation | NOT VERIFIED |
| VPS08-06 | **Real machine reboot** and post-reboot nginx/systemd/TLS/SQL/audit recovery | NOT VERIFIED |
| VPS08-07 | Inject failed binary deploy, automatic restore plus a deliberate compatible-binary rollback | NOT VERIFIED |
| VPS08-08 | Exact final SHA, all CI and staging logs, discovered/executed/skipped/failed counts, Owner exception register | NOT VERIFIED |

After Environment and host admission are Owner-verified under OWN-VPS-04, a protected manual staging run may be dispatched **only on main**:

    gh workflow run vps-08-linux-deploy.yml --repo chung6a8m/MyDmsVn-Vps --ref main -f reviewed_sha=EXACT_MAIN_SHA -f approved_scope=reviewed-isolated-sql13-only

The optional self-hosted job tests deployment, public TLS health, two direct-loopback bypass denials, manual service restart and SIGKILL recovery, after checking the independently approved machine fingerprint. It also creates an exclusive receipt state bound to the candidate SHA, machine hash and artifact manifest hash. It does **not** automatically certify host reboot, independent SQL Oracle reconciliation, full negative proxy matrix or a failed-deploy rollback.

After each real drill, create a sanitized event JSON containing `schemaVersion: 2`, `governanceMode: single-owner-exception`, `decisionId: OWN-VPS-04`, `assurance: NO_INDEPENDENT_HUMAN_REVIEW`, the fixed `event`, the same `sourceSha`, `machineSha256` and `owner: chung6a8m` as receipt state, UTC `observedAtUtc`, outcome, and SHA-256 `evidenceDigest` of separately retained raw evidence. The eight event names are fixed: `environment-admission`, `sql13-tls-oracle`, `nginx-proxy`, `systemd-audit`, `restart-sigkill`, `reboot-resume`, `failed-deploy-rollback`, and `final-reconciliation`. Normal events use `PASS`; the injected broken deployment uses `EXPECTED_FAILURE_RECOVERED`, preserving that the attempted deployment failed even when rollback recovered service:

    python3 scripts/vps09_linux_receipt.py record --state /protected/run/state.json \
      --evidence /protected/sanitized/reboot-resume.event.json
    python3 scripts/vps09_linux_receipt.py record --state /protected/run/state.json \
      --evidence /protected/sanitized/failed-deploy-rollback.event.json
    python3 scripts/vps09_linux_receipt.py finalize --state /protected/run/state.json \
      --output /protected/run/vps08-linux-receipt.json

`finalize` verifies structure and hashes only. Its state remains `OWNER_SECOND_PASS_REQUIRED`; it is not permission to close #11. After the VPS-09 24-hour cooling period, the Owner must compare the receipt with original logs, process identities, TLS and SQL queries, then record VPS08-01..08 as PASS with zero skipped/failed gates. Record cleanup separately with exact machine identity, versions, TLS chain, sanitized commands, audit/SQL expected vs actual and tested backup recovery. Every closeout record must state `NO_INDEPENDENT_HUMAN_REVIEW`. Never treat a skipped job as passed.
