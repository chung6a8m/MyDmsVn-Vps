// Runs only in the disposable, self-contained VPS-04A CI SQL job.
#if VPS04A_LIVE
using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps04aCiLocalSimulationOracleSqlAcceptanceTests
{
    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
            ? value : throw new InvalidOperationException("Missing required VPS04A CI setting: " + key);

    [Fact]
    public async Task ExerciseOracleRejectsExtraSyntheticReceiptAndJournal()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        var localSimulation = Assembly.Load("MyDmsVn.LocalSimulation");
        var oracleType = localSimulation
            .GetType("MyDmsVn.LocalSimulation.SqlLifecycle", throwOnError: true)!;
        var proofType = localSimulation
            .GetType("MyDmsVn.LocalSimulation.ExerciseProofCounts", throwOnError: true)!;
        var proofSql = (string)oracleType
            .GetField("ExerciseProofSql", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetRawConstantValue()!;
        var admin = new SqlConnectionStringBuilder(Required("VPS04A_CI_SQL_ADMIN_CONNECTION_STRING"))
        {
            InitialCatalog = "MyDmsVnVps_Ci",
            Pooling = false,
        };
        await using var sql = new SqlConnection(admin.ConnectionString);
        await sql.OpenAsync();
        await using var transaction = (SqlTransaction)await sql.BeginTransactionAsync();
        try
        {
            var operation = Guid.NewGuid();
            var note = "LOCAL-VERIFY-" + Guid.NewGuid().ToString("N");
            var actor = await ScalarAsync<int>(sql, transaction,
                "SELECT UserId FROM dbo.Users WHERE Username=@username;",
                ("@username", Required("VPS04A_LIVE_USERNAME")));
            var sale = await ScalarAsync<long>(sql, transaction, @"
INSERT dbo.SalesReceipts(SaleNo,SaleDate,WarehouseId,EmployeeId,CustomerId,Status,Note,CreatedByUserId)
OUTPUT INSERTED.SaleId
VALUES(@number,CONVERT(date,SYSUTCDATETIME()),@warehouse,@employee,@customer,'Draft',@note,@actor);",
                ("@number", "LS-" + Guid.NewGuid().ToString("N")[..29]),
                ("@warehouse", int.Parse(Required("VPS04A_LIVE_WAREHOUSE_ID"), CultureInfo.InvariantCulture)),
                ("@employee", int.Parse(Required("VPS04A_LIVE_EMPLOYEE_ID"), CultureInfo.InvariantCulture)),
                ("@customer", int.Parse(Required("VPS04A_LIVE_CUSTOMER_ID"), CultureInfo.InvariantCulture)),
                ("@note", note), ("@actor", actor));
            await ExecuteAsync(sql, transaction, @"
DECLARE @now datetime2(7)=SYSUTCDATETIME();
INSERT dbo.SalesReceiptLines(SaleId,[LineNo],ProductId,Qty,Unit,IsGift,LineAmount,UnitPrice,CreatedByUserId)
VALUES(@sale,1,@product,1,N'EA',0,2,2,@actor);
INSERT dbo.WebOperationJournal(ActorUserId,OperationId,DeploymentEpoch,ContractVersion,CodecVersion,OperationKind,
 RequestTargetId,RequestFingerprint,ResultDocumentId,Disposition,ResultBytes,CreatedAtUtc,CompletedAtUtc,ResultExpiresAtUtc)
SELECT @actor,@operation,DeploymentEpoch,1,1,1,NULL,HASHBYTES('SHA2_256',N'baseline'),@sale,'COMMITTED',0x7B7D,
 @now,@now,DATEADD(day,7,@now) FROM dbo.WebDeploymentState WHERE Id=1;",
                ("@sale", sale), ("@product", int.Parse(Required("VPS04A_LIVE_PRODUCT_ID"), CultureInfo.InvariantCulture)),
                ("@actor", actor), ("@operation", operation));

            var baseline = await ProofCountsAsync(sql, transaction, proofSql, sale, note, operation);
            Assert.Equal(new[] { 1, 1, 1, 0, 1, 1 }, baseline);
            Assert.True(IsValid(proofType, baseline));

            await ExecuteAsync(sql, transaction, @"
DECLARE @now datetime2(7)=SYSUTCDATETIME();
DECLARE @extraSale table(Id bigint NOT NULL);
INSERT dbo.SalesReceipts(SaleNo,SaleDate,WarehouseId,EmployeeId,CustomerId,Status,Note,CreatedByUserId)
OUTPUT INSERTED.SaleId INTO @extraSale
SELECT @number,SaleDate,WarehouseId,EmployeeId,CustomerId,'Draft',Note,CreatedByUserId
FROM dbo.SalesReceipts WHERE SaleId=@sale;
INSERT dbo.WebOperationJournal(ActorUserId,OperationId,DeploymentEpoch,ContractVersion,CodecVersion,OperationKind,
 RequestTargetId,RequestFingerprint,ResultDocumentId,Disposition,ResultBytes,CreatedAtUtc,CompletedAtUtc,ResultExpiresAtUtc)
SELECT TOP(1) u.UserId,j.OperationId,j.DeploymentEpoch,1,1,1,NULL,HASHBYTES('SHA2_256',N'extra'),x.Id,
 'COMMITTED',0x7B7D,@now,@now,DATEADD(day,7,@now)
FROM dbo.Users u CROSS JOIN dbo.WebOperationJournal j CROSS JOIN @extraSale x
WHERE u.UserId<>@actor AND j.ActorUserId=@actor AND j.OperationId=@operation ORDER BY u.UserId;",
                ("@number", "LS-" + Guid.NewGuid().ToString("N")[..29]), ("@sale", sale),
                ("@actor", actor), ("@operation", operation));

            var duplicates = await ProofCountsAsync(sql, transaction, proofSql, sale, note, operation);
            Assert.Equal(new[] { 1, 2, 1, 0, 1, 2 }, duplicates);
            Assert.False(IsValid(proofType, duplicates));
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task<T> ScalarAsync<T>(SqlConnection sql, SqlTransaction transaction, string statement,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new SqlCommand(statement, sql, transaction);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteAsync(SqlConnection sql, SqlTransaction transaction, string statement,
        params (string Name, object Value)[] parameters)
    {
        await using var command = new SqlCommand(statement, sql, transaction);
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int[]> ProofCountsAsync(SqlConnection sql, SqlTransaction transaction, string statement,
        long sale, string note, Guid operation)
    {
        await using var command = new SqlCommand(statement, sql, transaction);
        command.Parameters.Add("@sale", SqlDbType.BigInt).Value = sale;
        command.Parameters.Add("@note", SqlDbType.NVarChar, 500).Value = note;
        command.Parameters.Add("@operation", SqlDbType.UniqueIdentifier).Value = operation;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return Enumerable.Range(0, 6).Select(reader.GetInt32).ToArray();
    }

    private static bool IsValid(Type proofType, int[] counts)
    {
        var proof = Activator.CreateInstance(proofType, counts.Cast<object>().ToArray())!;
        return (bool)proofType.GetProperty("IsValid", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(proof)!;
    }
}
#endif
