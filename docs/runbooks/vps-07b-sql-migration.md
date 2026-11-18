# VPS-07B — safe SQL Server migration operator runbook

**Status:** DRAFT CANDIDATE under [PR #26](https://github.com/chung6a8m/MyDmsVn-Vps/pull/26), **not approved for production or staging**. Owner decision [OWN-VPS-02](../decisions/OWN-VPS-02.md) mandates fresh and independently proven existing-database paths. Protected staging is VPS-08 and release authorization belongs to VPS-09.

## Preconditions and trust boundary

1. Obtain explicit Owner/operator authorization for the exact deployment SHA, database identity, target schema and maintenance window. **No migration automatically runs when HttpHost starts.** The existing net48 DbUp command stays available for legacy tests; never run DbUp and FM independently against the same already-cutover database.
2. Provision a dedicated **database CONTROL** migration identity, **not** the API runtime identity or read-only oracle. SQL Server RESTRICTED_USER requires temporary membership in `db_owner` (or an independently approved equivalent restricted-access server role); this membership must be granted only for maintenance and revoked afterward. Do not use a normal `sysadmin` login for production migration. Original writer-drain validation additionally requires read-only server DMV visibility: `VIEW SERVER STATE`, `VIEW ANY DEFINITION` and on SQL Server 2022+ `VIEW SERVER PERFORMANCE STATE`; an explicitly authorized DBA must approve these grants and their revocation schedule. Do not use the sysadmin login as the normal production migration principal.
3. Confirm an *application database*, never `master`, `model`, `msdb`, or `tempdb`. The SQL client connection must specify `Initial Catalog`, `Encrypt=True`, `TrustServerCertificate=False`, a trusted certificate chain and matching hostname. Database secrets must be injected by an approved secret manager, never placed in command arguments, source control, screenshots or CI logs.
4. Stop or drain all application services, desktop API writers and operator jobs. Set and independently verify `RESTRICTED_USER` under DBA supervision. The CLI **does not** kill sessions, change database access modes, elevate itself, provision login rights or relax maintenance checks. A short bounded (up to three 200 ms) repeat of the **same** read-only DMV admission is allowed only for a transient `Migration.WritersNotDrained` race under the existing migration application lock; an active writer still blocks all attempts. It does not wait for a maintenance window, suppress an error or mutate journals. Independently verify no outstanding transactions or active writers.
5. Take an off-host protected SQL backup and a tested restore before touching historical data. Example for the DBA's already-approved database and path, not an automatic script:

```sql
BACKUP DATABASE [ApprovedAppDatabase]
TO DISK = N'<approved off-host backup path>'
WITH COPY_ONLY, INIT, CHECKSUM;
RESTORE VERIFYONLY FROM DISK = N'<approved off-host backup path>'
WITH CHECKSUM;
```

Record backup media ID, cryptographic digest, restore destination, operator and timestamp in private release evidence. A disposable Docker `BACKUP/RESTORE` CI rehearsal does **not** substitute for a production backup.

## Exact admission policy

| Source state | Allowed operation |
| --- | --- |
| Truly empty app DB with no user objects or migration journals | Fresh, explicitly target schema10/12/13 |
| Exact DbUp schema9/10/11/12 (native frozen fingerprint, ordered original `SchemaVersions`, writer drain) | Bridge *only completed versions*, then run forward to explicitly selected target |
| Exact DbUp schema13 | Bridge 1..13, then verified no-op; journal history never rewritten |
| Exact matching dual journal already present | Verified forward-only migration or no-op |
| Unknown, partial, missing/duplicate/reordered/future journal, altered object/permission or insufficient maintenance visibility | **REFUSE** without intentional schema mutation |
| Failed/interrupted upgrade | STOP; reconcile source rows, ledger/value balances, both journals and native schema hash before retry or backup restore |

Native schema fingerprinting excludes **only** the specifically proven `dbo.FluentMigratorVersionInfo` table signature. A database-scope extended-property marker `MyDmsVn.FluentMigratorBridge=vps07b-v1` is written atomically with the initial FM journal; missing/forged markers and deleted FM journal **must be treated as corruption**, never auto-restamped. The original `dbo.SchemaVersions` table and scripts 001–013 remain untouched. Direct PUBLIC grants to application-owned objects are refused before mutation.

## Command and release gates

Only after all prerequisites and independent approval:

```bash
export VPS07B_SQL_CONNECTION_STRING='<secret-manager-provided verified SQL connection>'
export VPS07B_OPERATOR_APPROVAL='approved-maintenance-and-verified-backup'
dotnet MyDmsVn.Server.FluentMigrator.Cli.dll migrate schema13
```

Choose `schema10` or `schema12` only when explicitly intended. Exit 0 means the guarded CLI completed and verified both journals and native schema; 2 denotes invalid options/operator approval, 4 preflight refusal, and 1 unresolved migration failure. **Exit 0 is not a production release approval**: separately compare read-only SQL row counts, source/stock ledger, valuation/COGS, sessions, protected operation records and owned application grants. Record exact SHA, SQL version, journal hashes, backup, stop/start timestamps and independent reviewer. Do not expose raw SQL error/row data in shared logs.

For failure: keep writers stopped, capture sanitized error code and database state, do not truncate/drop either journal, do not use `Down()` or manually backfill versions. Independent DBA must decide whether a verified retry is safe or restore the tested backup. Re-run schema and HTTP admission before restarting services. Production release, systemd, reverse proxy, secrets and rollback remain blocked on VPS-08/VPS-09.
