# Security administration — baseline operator reference (net48)

**Scope:** independent MyDmsVn-Vps documentation of the *imported* Security Administration command entry points, not production deployment approval. This file intentionally replaces the absent, source-era operational document required by an existing Architecture test.

## Current command surface

The legacy `src/MyDmsVn.Server.DbMigrator/Program.cs` dispatches:

- `security bootstrap-superuser` — bootstrap elevated administrator.
- `security recover-superuser` — recovery path.
- `security rename-superuser` — rename path.
- `permissions` — permission catalog command.
- No args, `--web` and `--web5` — **database migration modes**, not routine security administration commands.

The implementation resides under `src/MyDmsVn.Server.DbMigrator/SecurityProvisioning/`. Exact parameters, safe input restrictions, audit and database privileges are enforced in the code. Before any privileged invocation, inspect corresponding `*Command.cs`, understand required authenticated database context, and use a controlled non-production environment. Do not transmit secrets in command-line arguments, logs, PRs or chat.

## Restrictions

- Do not assume the baseline admin CLI is Linux-compatible; the current DbMigrator is `net48` and not yet migrated to FluentMigrator.
- Do not assume previously provisioned accounts or approvals transfer into this repository.
- Do not execute security provisioning or migrations merely as a CI smoke check.
- Validate permissions, audit and rollback in separate scoped SQL tests before operational authorization.
- Changes to authentication, permission data and recovery require independent approval and regression evidence (VPS-02, VPS-04, VPS-07 and VPS-09).

This runbook records current entry points only. It is not a substitute for operator-specific provisioning instructions or the approved Linux deployment runbook (VPS-08).
