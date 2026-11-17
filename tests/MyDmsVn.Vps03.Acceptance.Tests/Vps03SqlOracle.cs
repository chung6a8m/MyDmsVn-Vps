using System.IO;
using Microsoft.Data.SqlClient;
using MyDmsVn.Server.Infrastructure.Operations;
using System.Threading;
using System.Threading.Tasks;

namespace MyDmsVn.Vps03.Acceptance.Tests;

internal sealed class Vps03RuntimePrincipalSnapshot
{
    internal Vps03RuntimePrincipalSnapshot(bool hasDesktopRole, bool isSysAdmin,
        bool isDbOwner, bool hasUnexpectedWrite)
    {
        HasDesktopRole = hasDesktopRole;
        IsSysAdmin = isSysAdmin;
        IsDbOwner = isDbOwner;
        HasUnexpectedWrite = hasUnexpectedWrite;
    }

    internal bool HasDesktopRole { get; }
    internal bool IsSysAdmin { get; }
    internal bool IsDbOwner { get; }
    internal bool HasUnexpectedWrite { get; }
}

internal sealed class Vps03SqlOracle
{
    internal Vps03SqlOracle(string connectionString) => ConnectionString = connectionString;
    private string ConnectionString { get; }

    internal async Task<string> SchemaFingerprintAsync(CancellationToken token)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        return await SqlSchemaDefinitionFingerprint.ReadAsync(connection, token).ConfigureAwait(false);
    }

    internal async Task<Vps03RuntimePrincipalSnapshot> RuntimePrincipalAsync(CancellationToken token)
    {
        using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(token).ConfigureAwait(false);
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT
CONVERT(int,ISNULL(IS_ROLEMEMBER(N'MyDmsVnDesktopApiRuntimeRole'),0)),
CONVERT(int,ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0)),
CONVERT(int,ISNULL(IS_ROLEMEMBER(N'db_owner'),0)),
CONVERT(int,ISNULL(HAS_PERMS_BY_NAME(N'dbo.SchemaVersions',N'OBJECT',N'UPDATE'),0));";
        using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false))
            throw new InvalidDataException("VPS-03 runtime principal query returned no row.");
        return new Vps03RuntimePrincipalSnapshot(reader.GetInt32(0) == 1, reader.GetInt32(1) == 1,
            reader.GetInt32(2) == 1, reader.GetInt32(3) == 1);
    }

    internal static void ValidateRuntimePrincipal(Vps03RuntimePrincipalSnapshot snapshot)
    {
        if (!snapshot.HasDesktopRole || snapshot.IsSysAdmin || snapshot.IsDbOwner || snapshot.HasUnexpectedWrite)
            throw new InvalidDataException("VPS-03 HTTP runtime principal is missing its narrow role or has elevated permissions.");
    }
}
