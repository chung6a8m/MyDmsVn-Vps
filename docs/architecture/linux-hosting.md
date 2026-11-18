# Linux VPS hosting — VPS-08 implementation candidate, deployment NOT VERIFIED

**Decision:** [OWN-VPS-01](../decisions/OWN-VPS-01.md). **Implementation:** [VPS-08 draft PR #27](https://github.com/chung6a8m/MyDmsVn-Vps/pull/27). **Evidence authority:** [VPS-08 ledger](../evidence/vps-08-linux-deployment.md). No server, SQL13 staging or release approval is implied by these checked-in templates.

## Reference topology

`Windows Desktop net48 -> HTTPS/JSON -> Nginx :443 (validated TLS) -> 127.0.0.1:5080 Kestrel ASP.NET Core net10 -> restricted SQL Server with verified TLS`.

- Only the explicit `MYDMSVN_VPS08_PROXY_MODE=loopback-nginx-v1` enables reverse proxy deployment. Require exact localhost binding, single trusted loopback Nginx peer, exact Host, one `X-Forwarded-Proto=https`, one client IP, private proxy token, and a fixed `KnownProxies` allowlist. Reject unknown forwarding; do not enable global forwarded-header shortcuts. Default direct HTTPS remains unchanged.
- Nginx terminates TLS, **overwrites** rather than appends client-supplied forwarding, strips unknown variants and uses a root-only proxy token include. Public Kestrel port exposure and insecure `TrustServerCertificate=True` are prohibited.
- Dedicated unprivileged systemd identity with `ProtectSystem=strict`, bounded capabilities and `LoadCredential`. The wrapper presents SQL/JWT/proxy secrets to the legacy environment parser; root and the service identity remain trusted, so do not co-host untrusted processes.
- Durable protected WEB6 audit on a pre-provisioned private `0700` directory/`0600` inode with no-follow, lock and fsync. Journal is separate from audit and must not be confused with a cryptographic tamper-evident log.
- `GET /api/v1/health` is anonymous and SQL/schema is admitted on startup, with protected-audit degraded health 503. It is **not** a continuous independent SQL readiness/Oracle probe. Run an external SQL/Oracle readiness monitor in staging and distinguish process liveness.
- Release binary, SHA-bound manifest (not a digital signature), versioned root-owned release folders, atomic current symlink and binary-only rollback. No schema reverse migration.

## Acceptance still required

VPS-08 protected independently reviewed SQL13 staging, GitHub Environment branch/reviewer and admin bypass restrictions, TLS and SQL identity, real Nginx/systemd restart/process-loss, host reboot, audit durability, failed deploy and compatible rollback, full final-HEAD CI and independent reviewer. Consult [operator runbook](../runbooks/vps-08-linux-deployment.md). Integrated Windows Desktop E2E and release Owner decision belong to VPS-09.

SQL Server placement, VPS provider, domain, certificate authority and production release date remain deployment-specific choices; none is silently assumed.
