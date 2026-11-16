# Linux VPS hosting — planned, not yet deployed

**Decision basis:** [OWN-VPS-01](../decisions/OWN-VPS-01.md). **Implementation gate:** VPS-04 then VPS-08.

Target process topology: reverse proxy (TLS termination and well-defined forwarded headers) -> Kestrel ASP.NET Core 10 service -> portable infrastructure -> SQL Server over authenticated/encrypted network. systemd owns process lifecycle, identity and restart policy.

## Future technical checks

1. No `System.Web`, OWIN/SystemWeb host or Windows ACL assumptions in published Server runtime.
2. Explicit trusted proxies/networks; forwarded headers accepted only from the reverse proxy, correct HTTPS redirects/HSTS and request size constraints.
3. Dedicated unprivileged systemd identity, minimal filesystem grants, no secrets committed or echoed, safe audit directory with tamper-aware retention.
4. `/health` style liveness/readiness distinguished (do not report SQL-ready just because process responds); structured logs and request IDs with no credential leakage.
5. Validated TLS, firewall, SQL connectivity and timeout behavior; backup/recovery and separate deploy/rollback runbook.
6. Run real Linux deployment and restart/recovery tests, record OS/image versions and SHA.

**Not chosen yet:** SQL Server host location, TLS provider, Nginx versus another reverse proxy, VPS provider, hosting domains or release date. Do not convert this plan into a deployed-state claim.
