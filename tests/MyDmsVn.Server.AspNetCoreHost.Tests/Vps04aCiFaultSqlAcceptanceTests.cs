// CI-only fault-injection acceptance: NEVER run against an operator staging
// database. Stage one uses an admin-installed throw AFTER the StockLedger
// INSERT; stage two runs only after the admin removes the trigger and confirms
// the original schema13 fingerprint. The HTTP runtime remains least-privileged.
#if VPS04A_LIVE
using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Xunit;

namespace MyDmsVn.Server.AspNetCoreHost.Tests;

public sealed class Vps04aCiFaultSqlAcceptanceTests
{
    private static string Required(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
        ? value : throw new InvalidOperationException("CI-only fault admission missing.");

    private static void Guard()
    {
        Assert.Equal("approved-container-only", Required("VPS04A_CI_EPHEMERAL_SQL"));
        Assert.Equal("approved-ci-only", Required("VPS04A_CI_FAULT_APPROVED"));
        Assert.Equal("https://localhost:50443", Required("MYDMSVN_WEB_ORIGIN"));
        Assert.Equal("web5", Required("MYDMSVN_WEB_OPERATION_MODE"));
    }

    private static string StatePath() =>
        Path.Combine(Required("VPS04A_CI_TLS_DIR"),"ci-sql-rollback-intent.json");

    private sealed class Gateway : IDisposable
    {
        private readonly HttpClient _client=new(new HttpClientHandler {UseCookies=false})
        { BaseAddress=new Uri(Required("MYDMSVN_WEB_ORIGIN")+"/"),Timeout=TimeSpan.FromSeconds(40)};
        public async Task<JsonDocument> SendAsync(HttpMethod method, string path, int status,
            string? token=null,object? payload=null,string? epoch=null,Guid? key=null)
        {
            using var request=new HttpRequestMessage(method,path.TrimStart('/'));
            if(method!=HttpMethod.Get)
                request.Headers.TryAddWithoutValidation("Origin",Required("MYDMSVN_WEB_ORIGIN"));
            if(path=="/api/v1/auth/login")
                request.Headers.TryAddWithoutValidation("X-Requested-With","XMLHttpRequest");
            if(token!=null)request.Headers.TryAddWithoutValidation("Authorization","Bearer "+token);
            if(epoch!=null)request.Headers.TryAddWithoutValidation("X-MyDmsVn-Deployment-Epoch",epoch);
            if(key.HasValue)request.Headers.TryAddWithoutValidation("Idempotency-Key",key.Value.ToString("D"));
            if(payload!=null)request.Content=new StringContent(JsonSerializer.Serialize(payload),Encoding.UTF8,"application/json");
            else if(method!=HttpMethod.Get)request.Content=new ByteArrayContent(Array.Empty<byte>());
            using var response=await _client.SendAsync(request);
            Assert.Equal(status,(int)response.StatusCode);
            Assert.True(response.Headers.TryGetValues("Cache-Control",out var cache) &&
                cache.Any(v=>v.Contains("no-store",StringComparison.OrdinalIgnoreCase)));
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
        public void Dispose()=>_client.Dispose();
    }

    private static async Task<(string Token,string Epoch)> AdmitAsync(Gateway g)
    {
        using var login=await g.SendAsync(HttpMethod.Post,"/api/v1/auth/login",200,payload:new{
            username=Required("VPS04A_LIVE_USERNAME"),password=Required("VPS04A_LIVE_PASSWORD")});
        var token=login.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        using var context=await g.SendAsync(HttpMethod.Get,"/api/v1/operations/context",200,token);
        var epoch=context.RootElement.GetProperty("data").GetProperty("deploymentEpoch").GetString()!;
        return(token,epoch);
    }

    private static async Task<(decimal Quantity,decimal Value)> StockAsync(int wh,int product)
    {
        using var c=new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await c.OpenAsync();
        using var cmd=new SqlCommand(
            "SELECT Quantity,InventoryValue FROM dbo.StockBalances WHERE WarehouseId=@wh AND ProductId=@product;",c);
        cmd.Parameters.Add("@wh",SqlDbType.Int).Value=wh;
        cmd.Parameters.Add("@product",SqlDbType.Int).Value=product;
        using var reader=await cmd.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var pair=(reader.GetDecimal(0),reader.GetDecimal(1));
        Assert.False(await reader.ReadAsync());
        return pair;
    }

    private static async Task<int> CountAsync(string query,string field,object value)
    {
        using var c=new SqlConnection(Required("VPS04A_SQL_ORACLE_CONNECTION_STRING"));
        await c.OpenAsync();
        using var cmd=new SqlCommand(query,c);
        cmd.Parameters.AddWithValue(field,value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(),CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task InjectedLedgerFailureRollsBackSourceCostBalanceAndJournal()
    {
        Guard();
        Assert.False(File.Exists(StatePath()),"Fresh fault run must not reuse previous intent.");
        var wh=int.Parse(Required("VPS04A_LIVE_WAREHOUSE_ID"),CultureInfo.InvariantCulture);
        var product=int.Parse(Required("VPS04A_LIVE_PRODUCT_ID"),CultureInfo.InvariantCulture);
        var before=await StockAsync(wh,product);
        Assert.True(before.Quantity>=1m && before.Value>=10m,"Normal stock-reconciliation phase must precede injection.");
        using var g=new Gateway();
        var (token,epoch)=await AdmitAsync(g);
        var today=DateTime.UtcNow.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
        var draft=new{
            saleDate=today,
            customerId=int.Parse(Required("VPS04A_LIVE_CUSTOMER_ID"),CultureInfo.InvariantCulture),
            warehouseId=wh,employeeId=int.Parse(Required("VPS04A_LIVE_EMPLOYEE_ID"),CultureInfo.InvariantCulture),
            note="CI-FAULT-CHECK",
            lines=new[]{new {saleLineId="0",productId=product,qty="1.0000",unitPrice="20.0000",isGift=false}}
        };
        using var created=await g.SendAsync(HttpMethod.Post,"/api/v1/sales-receipts",200,token,draft,epoch,Guid.NewGuid());
        var data=created.RootElement.GetProperty("data");
        var saleId=long.Parse(data.GetProperty("saleId").GetString()!,CultureInfo.InvariantCulture);
        var version=data.GetProperty("version").GetString()!;
        var op=Guid.NewGuid();

        // The CI-only trigger raises after StockLedger INSERT. This is a
        // genuine SQL failure in the *middle* of the posted UoW, not a mock.
        using(var failed=await g.SendAsync(HttpMethod.Post,
            "/api/v1/sales-receipts/"+saleId+"/post",500,token,
            new {expectedVersion=version},epoch,op))
        {
            var error=failed.RootElement.GetProperty("error");
            Assert.Equal("InternalError",error.GetProperty("code").GetString());
            Assert.False(failed.RootElement.TryGetProperty("data",out _));
        }

        Assert.Equal(before,await StockAsync(wh,product));
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@key;",
            "@key",op));
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.StockLedger WHERE DocumentType='SalesReceipt' AND DocumentId=@id;",
            "@id",saleId));
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.StockValuationEntries v JOIN dbo.StockLedger l ON l.LedgerId=v.LedgerId WHERE l.DocumentType='SalesReceipt' AND l.DocumentId=@id;",
            "@id",saleId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@id AND Status='Draft' AND PostedAtUtc IS NULL AND PostedByUserId IS NULL;",
            "@id",saleId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceiptLines WHERE SaleId=@id AND PostedUnitCost IS NULL AND CostAmount IS NULL;",
            "@id",saleId));

        // Safe, secret-free local state for the separately gated recovery
        // phase only. No JWT, refresh cookie, SQL credential or personal data.
        File.WriteAllText(StatePath(),JsonSerializer.Serialize(new {
            saleId,version,op=op.ToString("D"),epoch,
            wh,product,beforeQty=before.Quantity,beforeValue=before.Value
        }));
    }

    [Fact]
    public async Task DisarmedOriginalSameKeyRetryCommitsExactlyOnce()
    {
        Guard();
        Assert.True(File.Exists(StatePath()),"Must first prove a real SQL rollback.");
        using var state=JsonDocument.Parse(File.ReadAllBytes(StatePath()));
        var data=state.RootElement;
        var saleId=data.GetProperty("saleId").GetInt64();
        var version=data.GetProperty("version").GetString()!;
        var op=Guid.Parse(data.GetProperty("op").GetString()!);
        var wh=data.GetProperty("wh").GetInt32();
        var product=data.GetProperty("product").GetInt32();
        var beforeQty=data.GetProperty("beforeQty").GetDecimal();
        var beforeValue=data.GetProperty("beforeValue").GetDecimal();
        using var g=new Gateway();
        var (token,epoch)=await AdmitAsync(g);
        Assert.Equal(data.GetProperty("epoch").GetString(),epoch);

        using(var committed=await g.SendAsync(HttpMethod.Post,
            "/api/v1/sales-receipts/"+saleId+"/post",200,token,
            new {expectedVersion=version},epoch,op))
        {
            var result=committed.RootElement.GetProperty("data");
            Assert.Equal("Posted",result.GetProperty("status").GetString());
            Assert.Equal(saleId,long.Parse(result.GetProperty("saleId").GetString()!,CultureInfo.InvariantCulture));
            Assert.True(result.TryGetProperty("version",out var newVersion) && newVersion.ValueKind==JsonValueKind.String);
        }

        Assert.Equal((beforeQty-1m,beforeValue-10m),await StockAsync(wh,product));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@key AND Disposition='COMMITTED';",
            "@key",op));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.StockLedger WHERE DocumentType='SalesReceipt' AND DocumentId=@id;",
            "@id",saleId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceipts WHERE SaleId=@id AND Status='Posted' AND PostedAtUtc IS NOT NULL;",
            "@id",saleId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.SalesReceiptLines WHERE SaleId=@id AND CostAmount=10.0000 AND PostedUnitCost IS NOT NULL;",
            "@id",saleId));

        // Replaying the immutable same-key intent must not double-post.
        using(var replay=await g.SendAsync(HttpMethod.Post,
            "/api/v1/sales-receipts/"+saleId+"/post",200,token,
            new {expectedVersion=version},epoch,op))
            Assert.Equal("Posted",replay.RootElement.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal((beforeQty-1m,beforeValue-10m),await StockAsync(wh,product));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.WebOperationJournal WHERE OperationId=@key;",
            "@key",op));
        File.Delete(StatePath());
    }
}
#endif
