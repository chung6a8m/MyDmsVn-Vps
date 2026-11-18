# VPS-08 — Linux VPS operator deployment and hardening

Scope: [Issue #11](https://github.com/chung6a8m/MyDmsVn-Vps/issues/11), [PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27), Owner decisions OWN-VPS-01/02. This document is an **operator procedure**, not proof that a VPS or protected SQL environment exists. Live status belongs only to [roadmap README](../roadmap/README.md).

## Target and trust boundaries

Reference design: Nginx with validated HTTPS certificate at the public hostname; private ASP.NET Core net10 Kestrel only at 127.0.0.1:5080; systemd dedicated unprivileged identity mydmsvn; restricted SQL Server runtime principal over verified encrypted TLS. SQL Server need not be on the VPS. In VPS-08 reverse-proxy mode, the Host also validates that SqlClient is configured for mandatory/strict encrypted transport with server certificate verification; optional encryption or TrustServerCertificate=true refuses startup. Nginx overwrites X-Forwarded-For with the immediate client IP, sends exactly https as X-Forwarded-Proto, preserves the exact public Host, and supplies a private proxy token from a root-owned include. Kestrel rejects unauthenticated, unknown, chained and non-loopback forwarding before ASP.NET Core forwards only For and Proto from 127.0.0.1. Never set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true, expose Kestrel publicly or use a wildcard known proxy.

Three secrets (restricted SQL string, JWT signing allowlist and proxy token) are loaded via systemd LoadCredential into the wrapper and exported to the child process. The existing application reads them from environment: they remain accessible to root and the same service identity. Do not co-host untrusted code under that user. Nginx and Kestrel must use the identical independently generated token (at least 32 bounded ASCII characters), never committed to GitHub or printed in logs. Rotate it by a coordinated service+proxy change. The non-secret runtime.env must contain the exact HTTPS public origin and issuer, web5/exact13 mode, existing superuser binding and approved WEB6 protected candidate values.

## Protected staging prerequisites

1. Independently reviewed **isolated nonproduction SQL13**, compatible verified DbUp/FluentMigrator journal bridge and schema fingerprint, tested backups/restores, restricted runtime principal and a **separate read-only SQL Oracle**. All SQL connections verify a trusted CA and hostname with Encrypt=True and TrustServerCertificate=False. No production account, database or shared backup. The Host must never auto-migrate. Use [VPS-07B migration procedure](vps-07b-sql-migration.md).
2. Dedicated staging Linux machine with monitored disk, systemd, Nginx, ASP.NET Core Runtime 10, approved firewall and TLS certificate. Allow external 443 only, never internet 5080/SQL. Configure protected audit pre-provisioning from the [VPS-04B runbook](vps-04b-protected-audit.md): private candidate root 0700, regular file 0600 created only once with exclusive/no-follow flags and fsync. Do not truncate or independently rotate the audit file.
3. GitHub Environment named vps08-sql13-nonproduction, a reviewer **independent of the actor**, prevent self review, admin bypass disabled, only exact main branch allowed, secret name VPS08_STAGING_HOST_ATTESTATION, variables VPS08_STAGING_PROBE_URL and VPS08_STAGING_MACHINE_SHA256 (SHA-256 of the independently reviewed host /etc/machine-id; do not publish the raw identifier), and a dedicated nonproduction self-hosted runner labeled vps08-staging and operating as vps08runner. No staging job can run on normal PR or push. GitHub Environment configuration is an external admin step, not established by CI.
4. Pin the full reviewed 40-character main SHA, reviewed artifact and complete same-SHA required CI. Confirm external Owner authorization to use staging SQL and ensure backup/restore and sensitive host cleanup plan are approved.

An independent admin can configure and verify (with locally authenticated gh CLI):

    python3 scripts/configure-vps08-staging-gate.py configure --reviewer INDEPENDENT_LOGIN
    # Manually disable admin bypass and set environment secret/variable names
    python3 scripts/configure-vps08-staging-gate.py verify --expected-sha EXACT_MAIN_SHA

The helper checks protection and names, **not secret values or SQL/VPS reality**. The previous PR #9 SQL job skipped by design and all disposable GitHub Actions SQL containers are not staging acceptance.

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

The root-owned rollout helper refuses symlinks, group/other-writable artifacts or paths, special file modes, concurrent deploy/rollback (exclusive flock), mutated package files and an existing release directory. A failed health check is never accepted solely because Nginx responds if systemd has stopped. It installs to /opt/mydmsvn/releases/FULL_SHA, atomically changes current, restarts systemd and polls publicly validated TLS health. On failed health it attempts restoring the previously manifest-verified binary and **returns failure even if recovery succeeds**. Rollback never downgrades SQL or undoes transactions. Only use an old binary when SQL schema, auth sessions and wire compatibility are independently proven:

    sudo /usr/local/libexec/vps08-rollout rollback PREVIOUS_40_HEX_SHA https://staging.example/api/v1/health
    sudo /usr/local/libexec/vps08-rollout rollback CANDIDATE_40_HEX_SHA https://staging.example/api/v1/health

Verify a direct unauthenticated loopback HTTP request (Host matching staging domain) gets HTTP 400, while public HTTPS health succeeds. Wrong Host, tampered credential, chained/unknown forwarded headers must be refused. Preserve protected audit records and inspect systemd journal privately (never upload raw tokens, SQL strings or audit data). Monitor TLS expiration, process restarts, audit inode/disk capacity and DB reachability.

## Acceptance ledger: strict external evidence needed

| ID | Real staged test | Status absent actual protected VPS |
| --- | --- | --- |
| VPS08-01 | Reviewed SHA, protected Environment (independent reviewer, bypass disabled, exact main), dedicated runner | NOT VERIFIED |
| VPS08-02 | Isolated SQL13, fingerprints/journals, verified TLS, restricted Host user and separate read-only Oracle | NOT VERIFIED |
| VPS08-03 | Nginx TLS, proxy trust and negative Host/headers/direct HTTP | NOT VERIFIED |
| VPS08-04 | systemd identity, service credentials, WEB6 protected audit mode/fsync and fail closed | NOT VERIFIED |
| VPS08-05 | Actual service restart, SIGKILL/process-loss auto restart, health and SQL/audit reconciliation | NOT VERIFIED |
| VPS08-06 | **Real machine reboot** and post-reboot nginx/systemd/TLS/SQL/audit recovery | NOT VERIFIED |
| VPS08-07 | Inject failed binary deploy, automatic restore plus a deliberate compatible-binary rollback | NOT VERIFIED |
| VPS08-08 | Exact final SHA, all CI and staging logs, discovered/executed/skipped/failed counts, Owner exception register | NOT VERIFIED |

After Environment and host admission are independently verified, a protected manual staging run may be dispatched **only on main**:

    gh workflow run vps-08-linux-deploy.yml --repo chung6a8m/MyDmsVn-Vps --ref main -f reviewed_sha=EXACT_MAIN_SHA -f approved_scope=reviewed-isolated-sql13-only

The optional self-hosted job tests deployment, public TLS health, two direct-loopback bypass denials, manual service restart and SIGKILL recovery, after checking the independently approved machine fingerprint. It does **not** automatically certify host reboot, independent SQL Oracle reconciliation, full negative proxy matrix or a failed-deploy rollback. Record those separately with exact machine identity, versions, TLS chain, commands (sanitized), audit/SQL expected vs actual, tested backup recovery and cleanup. Never treat a skipped job as passed. Windows Desktop-to-Linux-to-SQL integrated acceptance and production release decision belong to VPS-09.
