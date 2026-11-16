# OWN-VPS-02 — SQL Server + safe migration for installed databases

- **Status:** APPROVED
- **Decision date:** 2026-11-16
- **Chosen option:** 1 — Support existing databases and fresh installations
- **Owner authority:** direct Owner choice during the MyDmsVn-Vps interview

## Decision

SQL Server remains the application database. A future transition from DbUp to FluentMigrator must support clean installation **and** explicitly verified upgrade paths from legitimate existing schema states. Data integrity takes precedence over an easy migration cutover.

## Consequences

- Preserve original scripts 001–013 as immutable historic compatibility inputs.
- Inventory journal `dbo.SchemaVersions` and all relevant schema states before designing any FluentMigrator bridge.
- Do not replay completed scripts, delete DbUp journal, auto-run destructive `Down()`, or accept unknown/mismatched schemas.
- Require real isolated SQL checks: fresh install, supported upgrade, schema fingerprint, journal mapping, interrupted migration, concurrency, retry and backup/restore rehearsal (VPS-07/VPS-09).

## Explicitly not decided

Which historical schema versions are supported, SQL Server topology, migration tool rollout date or use of production data. Supported paths must be proven, then documented.
