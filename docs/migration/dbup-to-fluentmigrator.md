# DbUp -> FluentMigrator — safe transition contract (VPS-07)

**Current:** net48 `Server.DbMigrator` uses DbUp; 13 historical scripts are embedded and their checksums are pinned under `docs/import/`. **Target:** net10.0 standalone FluentMigrator versioned migrations after Server replatform. This document is a proposal, **not** an executed migration.

## Safety principles

- Preserve historical `dbo.SchemaVersions` journal as immutable evidence of completed DbUp scripts; do not rename/drop it merely to satisfy the new tool.
- Introduce a separate FluentMigrator version journal or carefully reviewed bridge. Map exact script order and version, not filenames heuristically.
- Validate schema fingerprint, journal contents, constraints/indexes and migration preconditions *before* applying anything.
- Refuse unknown schema, missing/mutated/duplicated journal entries or broken partial state. No destructive rollback by default, no blind reapply of scripts 001–013.
- Use a database-scoped concurrency strategy to serialize runners; define transaction scope and SQL batch semantics explicitly.
- Backup/restore rehearsal and isolated data-retention verification precede any production upgrade.

## Test oracle

| Input | Expected result |
| --- | --- |
| Fresh empty SQL database | Full expected schema and journal |
| Each explicitly supported old schema (candidate states schema9..schema13) | Correct bridge/upgrade, no data loss, no duplicate history |
| Already current valid schema | Idempotent no-op |
| Unknown/mismatched schema/journal | Deterministic refusal, no mutation |
| Interrupted migration | Never falsely marked compatible; recover or refuse |
| Two concurrent migrators | Exactly one effective migrator |
| Failed/retried migration | Consistent journal and application data |

Supported old states remain unannounced until run against real SQL Server with independent oracles. Infrastructure target/SQL topology/backup responsibility can be refined in VPS-08 without revisiting the approved fresh-and-existing requirement.
