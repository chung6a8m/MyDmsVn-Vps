using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
namespace MyDmsVn.Server.Infrastructure.Operations;

/// <summary>
/// Accepts only the exact known FM journal table definition. All other user
/// objects remain inside the native schema9..13 fingerprint inventory.
/// </summary>
public static class SqlFluentMigratorVersionTableProof
{
    public static async Task VerifyAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = @"
IF OBJECT_ID(N'dbo.FluentMigratorVersionInfo', N'U') IS NULL SELECT 1;
ELSE SELECT CASE WHEN
    (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U'))=3
    AND EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U')
        AND c.name=N'Version' AND TYPE_NAME(c.user_type_id)=N'bigint' AND c.is_nullable=0)
    AND EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U')
        AND c.name=N'AppliedOn' AND TYPE_NAME(c.user_type_id)=N'datetime' AND c.is_nullable=0)
    AND EXISTS(SELECT 1 FROM sys.columns c WHERE c.object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U')
        AND c.name=N'Description' AND TYPE_NAME(c.user_type_id)=N'nvarchar' AND c.max_length=2048 AND c.is_nullable=1)
    AND (SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U') AND index_id>0)=1
    AND EXISTS(SELECT 1 FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
        JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
        WHERE i.object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U')
        AND i.name=N'UC_FluentMigratorVersion' AND i.is_unique=1 AND i.is_disabled=0
        AND i.is_primary_key=0 AND i.is_unique_constraint=0 AND i.has_filter=0
        AND ic.key_ordinal=1 AND c.name=N'Version'
        AND NOT EXISTS (SELECT 1 FROM sys.index_columns extra
            WHERE extra.object_id=i.object_id AND extra.index_id=i.index_id AND extra.index_column_id<>ic.index_column_id))
    AND NOT EXISTS(SELECT 1 FROM sys.default_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U'))
    AND NOT EXISTS(SELECT 1 FROM sys.check_constraints WHERE parent_object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U'))
    AND NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U') OR referenced_object_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U'))
    AND NOT EXISTS(SELECT 1 FROM sys.triggers WHERE parent_id=OBJECT_ID(N'dbo.FluentMigratorVersionInfo',N'U'))
    THEN 1 ELSE 0 END;";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) != 1)
            throw new InvalidOperationException("Schema.FluentMigratorJournalDefinitionMismatch");
    }
}
