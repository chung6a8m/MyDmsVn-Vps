# VPS-04B — Linux protected audit admission and recovery runbook

**Scope:** direct HTTPS Kestrel net10 protected WEB6 candidate on Linux; not systemd/proxy/staging or an authorization to deploy production. See [Issue #10](https://github.com/chung6a8m/MyDmsVn-Vps/issues/10), [code-HEAD evidence](../evidence/vps-04b-linux-security.md), Owner VPS-01 Linux decision.

## Pre-provision a private, durable destination (operator action only)

Choose a **dedicated unprivileged service account** (example: `mydmsvn`) with a trusted local path, not a network share, container overlay with undocumented durability, or an automatically rotating log. The candidate root and file must already exist before starting Kestrel. Run the following **only for a fresh, previously unused destination**; never truncate or recreate an existing audit file:

```bash
# The host itself must NEVER create or chmod this directory or file.
sudo install -d -o mydmsvn -g mydmsvn -m 0700 /var/lib/mydmsvn/audit
sudo -u mydmsvn python3 - <<'PY'
import os
path = "/var/lib/mydmsvn/audit/security.jsonl"
# Exclusive atomic creation refuses existing entries, including dangling symlinks.
fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
try:
    os.fsync(fd)
finally:
    os.close(fd)
directory = os.open(os.path.dirname(path), os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
try:
    os.fsync(directory)
finally:
    os.close(directory)
PY
stat -c '%a %U:%G %h %n' /var/lib/mydmsvn/audit /var/lib/mydmsvn/audit/security.jsonl
```

The root must be an owner-only real directory `0700`; file a regular owner-only `0600` inode with **one** hard link. Creation uses `O_EXCL | O_NOFOLLOW` rather than a check-then-truncate shell redirection; it refuses any existing destination. Never rerun it on an established audit log or bypass a refusal with `chmod`/`chown` of an unverified path. Every parent must be non-symlink, trusted-owner and non-writable by other users (root-owned sticky `/tmp` is a test-specific accepted case). The host uses `openat` with `O_NOFOLLOW`, inode validation, `flock` and `fsync` before audit write acknowledgement. A missing file, ACL/mode drift, symlink, hardlink or unwritable disk fails closed.

## Configuration admission

Required WEB6 configuration, in addition to existing SQL, verified HTTPS origin/issuer, JWT key, runtime principal and `web5` mode:

```text
MYDMSVN_WEB_OPERATION_MODE=web5
MYDMSVN_WEB6_RESEND_ELIGIBILITY=receipt-v1
MYDMSVN_WEB6_CANDIDATE_ID=<approved bounded candidate>
MYDMSVN_WEB6_CANDIDATE_ROOT=/var/lib/mydmsvn/audit
MYDMSVN_WEB_SECURITY_AUDIT_PATH=/var/lib/mydmsvn/audit/security.jsonl
```

Use an approved restricted secret-delivery mechanism; **never commit connection strings, JWT keys, passwords or audit record payloads**. On Linux the path must be an absolute canonical Unix path with no `..`, `.`, double separators, backslashes or controls; the audit file must be underneath the candidate root. The host does not trust unapproved forwarded headers. Reverse proxy trust, secret deployment and systemd remain VPS-08 gates.

## Failure and restart behavior

- Startup runs SQL schema/principal checks and audit durability checks before opening the listener. If startup refuses, inspect OS permissions, mount capacity and operator configuration **without** disabling audit or rerouting it to TraceSource.
- On an audit write failure, protected admitted authentication writes fail closed and audit readiness becomes degraded; `GET /api/v1/health` replies HTTP **503** JSON `SecurityAudit.Unavailable` with sanitized correlation. Do not interpret a later business HTTP 200 as full operational health.
- Repair permissions/storage **under operator change control**, preserve existing audit records, then restart the host after ensuring previous worker has stopped and no duplicate writer remains. Verify health plus durable log record count and restricted SQL session state before accepting new traffic.
- `flock` is advisory and the log is not a cryptographic tamper-evident journal; collect backups off-host, monitor disk usage, enforce retention/rotation only with controlled worker stops and tested restore per VPS-08.
- The full accepted CI rehearsal is `.github/workflows/vps-04a-ci-sql.yml`: old 15 SQL cases with WEB6 disabled, followed by new isolated WEB6-on positive, failed-write/degraded-health, refusal on mode drift and restarted-host recovery. This is **not** an instruction to target production SQL.

**Not yet verified:** systemd/reverse proxy, remote TLS termination, managed secrets, protected staging, production rollback, integrated Windows Desktop acceptance.
