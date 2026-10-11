using Ims.Api.Tests.Builders;
using Ims.Api.Tests.Harness;
using Ims.Domain;

namespace Ims.Api.Tests.Tests.Visibility;

/// <summary>
/// The Warehouse staff may receive stock without entering or seeing cost. The server gives their receipt the last known (average) cost of each
/// product in the warehouse, or zero for a product never stocked there, whatever cost the request carried, so a receipt without a price cannot drag
/// the average cost of the stock towards zero.
/// </summary>
[Collection(ApiCollection.Name)]
public class StaffReceiptCostTests(ApiFixture fixture)
{
    private int Warehouse => fixture.Reference.DefaultWarehouseId;

    [Fact]
    public async Task A_receipt_by_the_staff_takes_the_average_cost_and_leaves_the_average_unchanged()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 4m));

        var draft = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 5, UnitCost: 99m));
        await s.CompleteAsync(await s.SubmitAsync(s.Staff, draft));

        var line = Assert.Single((await s.GetAsync(draft.Id)).Lines);
        Assert.Equal((null, 4m, true), (line.UnitCost, line.BaseUnitCost, line.CostFromAverage)); // the cost in the request is ignored; the line is at the average
        Assert.Equal(15m, await s.OnHandAsync(product.Id, Warehouse));
        Assert.Equal(4m, (await s.BalanceAsync(product.Id, Warehouse))!.AvgCost);
        Assert.Null(Assert.Single(draft.Lines).UnitCost);              // and the staff never see it
    }

    [Fact]
    public async Task A_product_never_stocked_in_the_warehouse_is_received_at_zero_by_the_staff()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();

        var draft = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 5));

        var line = Assert.Single((await s.GetAsync(draft.Id)).Lines);
        Assert.Equal((null, 0m), (line.UnitCost, line.BaseUnitCost));
        Assert.Equal(0m, (await s.GetAsync(draft.Id)).TotalAmount);
    }

    [Fact]
    public async Task Saving_the_draft_again_as_staff_takes_the_cost_again()
    {
        var s = await StockScenario.CreateAsync(fixture);
        var product = await s.NewProductAsync();
        await s.ReceiveAsync(Warehouse, new Line(product.Id, 10, 4m));
        var draft = await s.DraftReceiptAsync(s.Staff, Warehouse, new Line(product.Id, 2));

        var request = new DocumentBuilder(Warehouse).WithPartner(fixture.Reference.SupplierId).WithLines(new Line(product.Id, 6, UnitCost: 50m)).Build();
        (await s.Staff.Documents.UpdateAsync(draft.Id, request)).ShouldBeOk();

        var edited = await s.GetAsync(draft.Id);
        Assert.Equal(4m, Assert.Single(edited.Lines).BaseUnitCost);
        Assert.Equal(24m, edited.TotalAmount);
    }
}
