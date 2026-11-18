# DbUp -> FluentMigrator — safe transition contract (VPS-07)

**Repository status (2026-11-18):** original net48 DbUp remains available as the legacy oracle. VPS-07A has been merged (PR #24); VPS-07B's standalone net10 FluentMigrator CLI, isolated dual-journal bridge and SQL acceptance harness were independently reviewed with 58/58 disposable SQL cases under [PR #26](https://github.com/chung6a8m/MyDmsVn-Vps/pull/26). **Only CI-confirmed disposable SQL test results count as verified; production cutover, protected staging and release remain NOT VERIFIED.**

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

## VPS-07B reviewed implementation — disposable SQL acceptance only

- Explicit command `migrate schema10|schema12|schema13`, strict operator acknowledgement and verified TLS with named application `Initial Catalog`. No web-startup migration, default target, system database or silent fallback. A separate non-pooled SQL session acquires the same exclusive `MyDmsVn.SchemaMigration` app lock as legacy DbUp.
- The *old* `dbo.SchemaVersions` remains canonical historical evidence. The strictly shaped `dbo.FluentMigratorVersionInfo` table and database-scope durable marker `MyDmsVn.FluentMigratorBridge=vps07b-v1` are committed atomically in one bridge transaction; the marker distinguishes legitimate first-time DbUp import from a missing/forged FM journal after cutover. A validated schema9..13 source is stamped only once; immutable SQL and both version appends share each forward migration transaction.
- Native source admission remains exact: schema9 legacy checker, schema10, schema11, schema12 or schema13 definition fingerprints, canonical full-name DbUp journal prefix/description checks, original restricted maintenance/writer-drain validator and refusal of PUBLIC grants on application objects, dbo schema or database scope; unknown/future/partial schemas fail closed. A narrowly attested FM table is excluded from the historical definition hash. Arbitrary additional objects are not accepted.
- On any failed or killed SQL migration, do **not** assume the physical database has rolled back: independently check both journals, native fingerprint and business rows before an authorized retry; restore an approved backup when state is partial. Fresh prefixes 0–8 are deliberately not auto-resumable: replace the verified never-used database or restore instead of guessing interim schema compatibility. `Down()` is prohibited.
- Isolated CI runs an independent original DbUp9..13 oracle and a fresh FM install comparison; checks data (receipts, ledger, balances, valuation, users, web sessions, operation journals, terminal audit) alongside schema definitions. Workstream acceptance additionally requires early/middle/late fault, process-loss retry, wrong privilege, writer drain and backup/restore evidence on the final PR HEAD.

See [VPS-07B runbook](../runbooks/vps-07b-sql-migration.md) and [working evidence](../evidence/vps-07b-journal-bridge.md). **Do not use the candidate CLI with a business database until independent review, all real SQL gates and Owner authorization.**
