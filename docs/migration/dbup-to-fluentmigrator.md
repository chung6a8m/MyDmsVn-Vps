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


## VPS-07A source implementation (2026-11-18)

The independent net10.0 library `src/MyDmsVn.Server.FluentMigrator` now holds 13 explicit
`[Migration(1..13)]` classes linking (not copying) the pinned original 001-013 SQL bytes.
Each wrapper executes the existing T-SQL in order, separating the 28 client-side
`GO` batches; `Down()` fails closed, with no destructive downgrade path.

VPS-07A does not change the DbUp net48 executable, journal or real database.
The standalone write-capable FluentMigrator CLI, version table, schema/journal bridge,
privilege/maintenance policy and production cutover remain **VPS-07B**, which must
be reviewed and accepted independently before legacy DbUp is retired.

VPS-07B must prove on isolated real SQL Server: fresh install and separately
admitted schema9, 10, 11, 12 and 13 paths; exact legacy journal, schema definition
fingerprints, and transaction-per-version semantics; no replay of applied SQL;
two migrating processes, failure/interruption, retry, data preservation, backup
and restore rehearsal. Unknown or partial states must be refused without mutation.

**Evidence status:** source/contract implementation passed 8/8 pre-evidence-commit CI workflows on `dd043ec2`, see [VPS-07A evidence](../evidence/vps-07a-fluentmigrator-foundation.md). Final-HEAD CI and review remain separate. The [VPS-07B real SQL journal bridge](https://github.com/chung6a8m/MyDmsVn-Vps/issues/25), protected staging, and production approval remain NOT VERIFIED.
