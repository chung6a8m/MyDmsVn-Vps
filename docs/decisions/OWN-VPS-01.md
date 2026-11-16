# OWN-VPS-01 — Deploy HTTP host on Linux VPS

- **Status:** APPROVED
- **Decision date:** 2026-11-16
- **Chosen option:** 1 — Linux VPS
- **Owner authority:** direct Owner choice during the MyDmsVn-Vps interview

## Decision

The future `MyDmsVn.Server.HttpHost` runs as ASP.NET Core 10 on a Linux VPS, using Kestrel behind a reverse proxy, managed by systemd. Desktop remains a Windows application and talks to the host through HTTPS/JSON.

## Consequences

- Port the HttpHost off Web API 2/OWIN/System.Web and remove Windows-only server runtime dependencies (VPS-04).
- Introduce Linux-compatible audit storage, trusted proxy configuration, service identity, TLS, health/observability, restart/recovery and secure deployment (VPS-04/VPS-08).
- Prove security and HTTP parity before accepting Linux runtime (VPS-02/VPS-04/VPS-09).

## Explicitly not decided

Location/OS of SQL Server, reverse proxy implementation, VPS provider, domain name, secret store, deployment topology and release date. These require later assessment; Linux VPS **does not imply Linux SQL Server**.
