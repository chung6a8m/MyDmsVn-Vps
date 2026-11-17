using System;
using System.Threading;
using System.Threading.Tasks;
using MyDmsVn.Contracts;
using MyDmsVn.Desktop.Application;
using MyDmsVn.Desktop.Infrastructure.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MyDmsVn.Vps03.Acceptance.Tests;

public sealed class Vps03TransactionAcceptanceTests
{
    [Vps03AcceptanceFact]
    [Trait("CaseId", "VPS03.GATE.LOST-ACK")]
    public async Task Lost_ack_and_concurrent_session_never_duplicate_sales_post()
    {
        var env = await Vps03OwnedEnvironment.CreateAsync(13, CancellationToken.None);
        try
        {
            Ok(await env.DesktopServices.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(env.Username, env.Password), default));
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 9);
            var product = Ok(await env.DesktopServices.GetRequiredService<IProductApiClient>()
                .CreateAsync(new SaveProductRequest("L" + suffix, "Lost ACK " + suffix, "EA"), default));
            var warehouse = Ok(await env.DesktopServices.GetRequiredService<IWarehouseApiClient>()
                .CreateAsync(new SaveWarehouseRequest("A" + suffix, "ACK Warehouse " + suffix, null), default));
            var employee = Ok(await env.DesktopServices.GetRequiredService<IEmployeeApiClient>()
                .CreateAsync(new SaveEmployeeRequest("B" + suffix, "ACK Employee " + suffix, null, null), default));
            var customer = Ok(await env.DesktopServices.GetRequiredService<ICustomerApiClient>()
                .CreateAsync(new SaveCustomerRequest("D" + suffix, "ACK Customer " + suffix, null, null, null), default));
            var goods = env.DesktopServices.GetRequiredService<IGoodsReceiptApiClient>();
            var goodsDraft = Ok(await goods.CreateDraftAsync(new SaveGoodsReceiptRequest(DateTime.Today,
                warehouse.Id, employee.Id, "lost ACK stock",
                new[] { new SaveGoodsReceiptLineRequest(product.Id, 10m, 5m) }), default));
            Ok(await goods.PostAsync(new PostGoodsReceiptRequest(goodsDraft.Id, goodsDraft.Version), default));
            var seedSales = env.DesktopServices.GetRequiredService<ISalesReceiptApiClient>()
                .ForSession(env.DesktopServices.GetRequiredService<IDesktopSession>().Version);
            var saleDraft = Ok(await seedSales.CreateDraftAsync(new SaveSalesReceiptRequest(DateTime.Today,
                customer.Id, warehouse.Id, employee.Id, "lost ACK sale",
                new[] { new SaveSalesReceiptLineRequest(0, product.Id, 2m, 12m, false) }), default));

            var route = "/api/v1/sales-receipts/" + saleDraft.SaleId + "/post";
            var store = new Vps03InMemoryPendingWriteStore();
            var proxy = new Vps03LostAckProxy(env.Origin, route);
            using var first = Vps03FaultedDesktopServices.Create(env, proxy, store);
            Ok(await first.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(env.Username, env.Password), default));
            var firstSales = first.GetRequiredService<ISalesReceiptApiClient>()
                .ForSession(first.GetRequiredService<IDesktopSession>().Version);
            var lost = await firstSales.PostAsync(
                new PostSalesReceiptRequest(saleDraft.SaleId, saleDraft.Version), default);
            Assert.False(lost.IsSuccess);
            Assert.Equal("Operation.OutcomeUnknown", lost.Error?.Code);
            Assert.Equal(1, proxy.MatchedRequests);
            Assert.Equal(1, proxy.DroppedResponses);
            Assert.NotNull(store.Load());
            Assert.Equal(new Snapshot(2, 2, 8m, 40m, 10m),
                await SnapshotAsync(env.OracleConnectionString, warehouse.Id, product.Id, goodsDraft.Id, saleDraft.SaleId));

            using var second = Vps03FaultedDesktopServices.Create(env,
                Vps03LostAckProxy.CreatePassThrough(), store);
            Ok(await second.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(env.Username, env.Password), default));
            var secondSales = second.GetRequiredService<ISalesReceiptApiClient>()
                .ForSession(second.GetRequiredService<IDesktopSession>().Version);
            var blocked = await secondSales.PostAsync(
                new PostSalesReceiptRequest(saleDraft.SaleId, saleDraft.Version), default);
            Assert.False(blocked.IsSuccess);
            Assert.Equal("Operation.PendingRecovery", blocked.Error?.Code);
            var recovered = await second.GetRequiredService<IDesktopWriteRecovery>()
                .CheckAsync(CancellationToken.None);
            Assert.Equal("COMMITTED", recovered.State);
            Assert.True(recovered.FenceReleased);
            Assert.Null(store.Load());
            Assert.Equal(new Snapshot(2, 2, 8m, 40m, 10m),
                await SnapshotAsync(env.OracleConnectionString, warehouse.Id, product.Id, goodsDraft.Id, saleDraft.SaleId));
        }
        finally { await env.DisposeAsync(); }
    }

    [Vps03AcceptanceFact]
    [Trait("CaseId", "VPS03.GATE.TRANSACTIONS")]
    [Trait("CaseId", "VPS03.GATE.INVENTORY")]
    public async Task Goods_and_sales_post_each_commit_one_ledger_valuation_and_cogs_effect()
    {
        var env = await Vps03OwnedEnvironment.CreateAsync(13, CancellationToken.None);
        try
        {
            Ok(await env.DesktopServices.GetRequiredService<IIdentityApiClient>()
                .LoginAsync(new LoginRequest(env.Username, env.Password), default));
            var suffix = Guid.NewGuid().ToString("N").Substring(0, 9);
            var product = Ok(await env.DesktopServices.GetRequiredService<IProductApiClient>()
                .CreateAsync(new SaveProductRequest("P" + suffix, "Product " + suffix, "EA"), default));
            var warehouse = Ok(await env.DesktopServices.GetRequiredService<IWarehouseApiClient>()
                .CreateAsync(new SaveWarehouseRequest("W" + suffix, "Warehouse " + suffix, null), default));
            var employee = Ok(await env.DesktopServices.GetRequiredService<IEmployeeApiClient>()
                .CreateAsync(new SaveEmployeeRequest("E" + suffix, "Employee " + suffix, null, null), default));
            var customer = Ok(await env.DesktopServices.GetRequiredService<ICustomerApiClient>()
                .CreateAsync(new SaveCustomerRequest("C" + suffix, "Customer " + suffix, null, null, null), default));

            var goods = env.DesktopServices.GetRequiredService<IGoodsReceiptApiClient>();
            var goodsDraft = Ok(await goods.CreateDraftAsync(new SaveGoodsReceiptRequest(DateTime.Today,
                warehouse.Id, employee.Id, "exactly-once goods",
                new[] { new SaveGoodsReceiptLineRequest(product.Id, 10m, 5m) }), default));
            Ok(await goods.PostAsync(new PostGoodsReceiptRequest(goodsDraft.Id, goodsDraft.Version), default));
            Assert.Equal(new Snapshot(1, 1, 10m, 50m, 0m),
                await SnapshotAsync(env.OracleConnectionString, warehouse.Id, product.Id, goodsDraft.Id, 0));
            var sales = env.DesktopServices.GetRequiredService<ISalesReceiptApiClient>()
                .ForSession(env.DesktopServices.GetRequiredService<IDesktopSession>().Version);
            var saleDraft = Ok(await sales.CreateDraftAsync(new SaveSalesReceiptRequest(DateTime.Today,
                customer.Id, warehouse.Id, employee.Id, "exactly-once sale",
                new[] { new SaveSalesReceiptLineRequest(0, product.Id, 2m, 12m, false) }), default));
            Ok(await sales.PostAsync(new PostSalesReceiptRequest(saleDraft.SaleId, saleDraft.Version), default));
            Assert.Equal(new Snapshot(2, 2, 8m, 40m, 10m),
                await SnapshotAsync(env.OracleConnectionString, warehouse.Id, product.Id, goodsDraft.Id, saleDraft.SaleId));
        }
        finally { await env.DisposeAsync(); }
    }

    private static async Task<Snapshot> SnapshotAsync(string connectionString, int warehouseId,
        int productId, long receiptId, long saleId)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.Parameters.AddWithValue("@warehouse", warehouseId);
        command.Parameters.AddWithValue("@product", productId);
        command.Parameters.AddWithValue("@receipt", receiptId);
        command.Parameters.AddWithValue("@sale", saleId);
        command.CommandText = @"SELECT
 (SELECT COUNT(*) FROM dbo.StockLedger WHERE WarehouseId=@warehouse AND ProductId=@product),
 (SELECT COUNT(*) FROM dbo.StockValuationEntries v JOIN dbo.StockLedger l ON l.LedgerId=v.LedgerId WHERE l.WarehouseId=@warehouse AND l.ProductId=@product),
 (SELECT Quantity FROM dbo.StockBalances WHERE WarehouseId=@warehouse AND ProductId=@product),
 (SELECT InventoryValue FROM dbo.StockBalances WHERE WarehouseId=@warehouse AND ProductId=@product),
 COALESCE((SELECT SUM(CostAmount) FROM dbo.SalesReceiptLines WHERE SaleId=@sale),0);";
        using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new Snapshot(reader.GetInt32(0), reader.GetInt32(1), reader.GetDecimal(2),
            reader.GetDecimal(3), reader.GetDecimal(4));
    }

    private static T Ok<T>(ApiResponse<T> response)
    { Assert.True(response.IsSuccess, response.Error?.Code + ": " + response.Error?.Message); Assert.NotNull(response.Data); return response.Data!; }

    private sealed class Snapshot : IEquatable<Snapshot>
    {
        internal Snapshot(int ledger, int valuation, decimal quantity, decimal value, decimal cogs)
        { Ledger = ledger; Valuation = valuation; Quantity = quantity; Value = value; Cogs = cogs; }
        internal int Ledger { get; } internal int Valuation { get; } internal decimal Quantity { get; }
        internal decimal Value { get; } internal decimal Cogs { get; }
        public bool Equals(Snapshot? other) => other != null && Ledger == other.Ledger && Valuation == other.Valuation &&
            Quantity == other.Quantity && Value == other.Value && Cogs == other.Cogs;
        public override bool Equals(object? obj) => Equals(obj as Snapshot);
        public override int GetHashCode() => Ledger ^ Valuation ^ Quantity.GetHashCode() ^ Value.GetHashCode() ^ Cogs.GetHashCode();
        public override string ToString() => $"ledger={Ledger}, valuation={Valuation}, qty={Quantity}, value={Value}, cogs={Cogs}";
    }
}
