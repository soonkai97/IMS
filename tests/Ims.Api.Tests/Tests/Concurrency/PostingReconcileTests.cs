using System.Net;
using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application.Features.Documents;

namespace Ims.Api.Tests.Tests.Concurrency;

/// <summary>
/// The reconcile drill (docs/dba/08-reconcile-drill.sql) as a test: after receipts and issues of one product in one warehouse are posted at the same
/// instant, the stored balance equals the sum of the ledger and every ledger row carries the running balance it should, read straight from the database.
/// </summary>
[Collection(ApiCollection.Name)]
public class PostingReconcileTests(ApiFixture fixture)
{
    private const decimal OpeningQuantity = 500m;
    private const int ReceiptCount = 15;
    private const int IssueCount = 15;
    private const decimal IssueQuantity = 3m;
    private const decimal FirstReceiptQuantity = 4m; // receipt n brings FirstReceiptQuantity + n, at a cost that differs from its neighbours
    private const decimal OpeningCost = 2m;
    private const decimal ReceiptCostBase = 3m;
    private const int ReceiptCostSteps = 3;

    private Guid Warehouse => fixture.Reference.DefaultWarehouseId;

    /// <summary>The ledger and the balances are keyed by the integer ids; the test names the product and the warehouse by the ids the API gives.</summary>
    private async Task<T> Sql<T>(string sql, Guid product, Guid warehouse)
    {
        var productId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM Product WHERE PublicId = @p", ("@p", product));
        var warehouseId = await fixture.Database.ScalarAsync<int>("SELECT Id FROM Warehouse WHERE PublicId = @w", ("@w", warehouse));
        return await fixture.Database.ScalarAsync<T>(sql, ("@p", productId), ("@w", warehouseId));
    }

    [Fact]
    public async Task Parallel_receipts_and_issues_of_one_product_leave_the_balance_equal_to_the_ledger_and_every_balance_after_correct()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, OpeningQuantity, OpeningCost));
        var receipts = new List<DocumentDto>();
        var issues = new List<DocumentDto>();
        for (var i = 0; i < ReceiptCount; i++)
            receipts.Add(await s.SubmitAsync(s.Admin, await s.DraftReceiptAsync(s.Admin, Warehouse, new Line(product.Id, FirstReceiptQuantity + i, ReceiptCostBase + i % ReceiptCostSteps))));
        for (var i = 0; i < IssueCount; i++)
            issues.Add(await s.SubmitAsync(s.Admin, await s.DraftIssueAsync(s.Admin, Warehouse, new Line(product.Id, IssueQuantity))));
        var interleaved = receipts.Zip(issues, (receipt, issue) => new[] { receipt, issue }).SelectMany(pair => pair).ToList();

        var responses = await Concurrently.RunAsync(interleaved.Select(d => (Func<Task<ApiResponse>>)(() => s.Admin.Documents.PostAsync(d.Id))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.Status));
        var expected = OpeningQuantity + Enumerable.Range(0, ReceiptCount).Sum(i => FirstReceiptQuantity + i) - IssueCount * IssueQuantity;
        Assert.Equal(1 + ReceiptCount + IssueCount, await Sql<int>("SELECT COUNT(*) FROM StockLedgerEntry WHERE ProductId = @p AND WarehouseId = @w", product.Id, Warehouse));
        Assert.Equal(expected, await Sql<decimal>("SELECT SUM(QtyDelta) FROM StockLedgerEntry WHERE ProductId = @p AND WarehouseId = @w", product.Id, Warehouse));
        Assert.Equal(expected, await Sql<decimal>("SELECT QtyOnHand FROM StockBalance WHERE ProductId = @p AND WarehouseId = @w", product.Id, Warehouse));
        // The newest row says what the product holds now; every row says the running sum of its own product and warehouse in append order; none went below zero.
        Assert.Equal(expected, await Sql<decimal>("SELECT TOP 1 BalanceAfter FROM StockLedgerEntry WHERE ProductId = @p AND WarehouseId = @w ORDER BY Id DESC", product.Id, Warehouse));
        Assert.Equal(0, await Sql<int>("""
            SELECT COUNT(*) FROM (
                SELECT BalanceAfter, SUM(QtyDelta) OVER (ORDER BY Id ROWS UNBOUNDED PRECEDING) AS Running
                FROM StockLedgerEntry WHERE ProductId = @p AND WarehouseId = @w) x
            WHERE x.BalanceAfter <> x.Running
            """, product.Id, Warehouse));
        Assert.True(await Sql<decimal>("SELECT MIN(BalanceAfter) FROM StockLedgerEntry WHERE ProductId = @p AND WarehouseId = @w", product.Id, Warehouse) >= 0m);
    }

    [Fact]
    public async Task Every_balance_in_the_database_equals_its_ledger_and_ends_on_its_last_balance_after()
    {
        // Whatever the tests before this one posted, in whatever order: no balance without ledger rows, no ledger rows without a balance, no drift.
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 1m));

        var sumMismatches = await fixture.Database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM (SELECT ProductId, WarehouseId, SUM(QtyDelta) AS q FROM StockLedgerEntry GROUP BY ProductId, WarehouseId) l
            FULL JOIN StockBalance b ON b.ProductId = l.ProductId AND b.WarehouseId = l.WarehouseId
            WHERE ISNULL(b.QtyOnHand, -1) <> ISNULL(l.q, -2)
            """);
        var lastMismatches = await fixture.Database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM (SELECT ProductId, WarehouseId, MAX(Id) AS LastId FROM StockLedgerEntry GROUP BY ProductId, WarehouseId) m
            JOIN StockLedgerEntry l ON l.Id = m.LastId
            FULL JOIN StockBalance b ON b.ProductId = m.ProductId AND b.WarehouseId = m.WarehouseId
            WHERE ISNULL(l.BalanceAfter, -1) <> ISNULL(b.QtyOnHand, -2)
            """);
        var runningMismatches = await fixture.Database.ScalarAsync<int>("""
            SELECT COUNT(*) FROM (
                SELECT BalanceAfter, SUM(QtyDelta) OVER (PARTITION BY ProductId, WarehouseId ORDER BY Id ROWS UNBOUNDED PRECEDING) AS Running
                FROM StockLedgerEntry) x
            WHERE x.BalanceAfter <> x.Running
            """);

        Assert.Equal((0, 0, 0), (sumMismatches, lastMismatches, runningMismatches));
    }
}
