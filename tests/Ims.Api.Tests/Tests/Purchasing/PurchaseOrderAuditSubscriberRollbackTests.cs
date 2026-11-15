using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Application.Features.Purchasing;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Purchasing;

/// <summary>
/// DDD-12 against real SQL Server: the audit row that <c>PurchaseOrderAuditSubscriber</c> stages for an event commits or rolls back WITH the business change (AUTH-03: an audit row cannot be lost, and no row
/// describes a change that did not happen). The host is the real one, so the real subscriber runs; the unit of work is driven directly with real purchase orders. The rows of every verb through the HTTP
/// pipeline are pinned by <c>PurchaseOrderAuditRowsTests</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PurchaseOrderAuditSubscriberRollbackTests(ApiFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _host = null!;
    private int _warehouseId, _supplierId, _productId;

    public async Task InitializeAsync()
    {
        _host = fixture.CreateHost(_ => { });
        _ = _host.Services;

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var uom = Uom.Create(Unique.Code("U"));
        var warehouse = Warehouse.Create(Unique.Code("W"), Unique.Name("Warehouse"));
        var supplier = Partner.Create(PartnerType.Supplier, Unique.Name("Supplier"));
        var product = Product.Create(Unique.Sku(), Unique.Name("Product"), null, uom, 0m);
        db.AddRange(uom, warehouse, supplier, product);
        await db.SaveChangesAsync();
        (_warehouseId, _supplierId, _productId) = (warehouse.Id, supplier.Id, product.Id);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string NewNo() => $"PO-{Unique.Code("P")}";

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        using var scope = _host.Services.CreateScope();
        await body(scope.ServiceProvider);
    }

    /// <summary>A new Draft with one line, tracked as Added: raises <see cref="PurchaseOrderCreated"/>, which the subscriber turns into a row at the next save.</summary>
    private async Task StageOrderAsync(IServiceProvider sp, string no, string? remark = null)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var warehouse = await db.Set<Warehouse>().SingleAsync(w => w.Id == _warehouseId);
        var supplier = await db.Set<Partner>().SingleAsync(s => s.Id == _supplierId);
        var product = await db.Set<Product>().SingleAsync(p => p.Id == _productId);
        var order = PurchaseOrder.Create(warehouse, supplier, remark, 1, DateTime.UtcNow);
        order.AssignNumber(no);
        order.ReplaceLines([PurchaseOrderLine.Create(product, 5m, null, 10m, null)]);
        sp.GetRequiredService<IRepository<PurchaseOrder>>().Add(order);
    }

    private Task<int> OrdersAsync(string no) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder WHERE No = @n", ("@n", no));

    private Task<int> RowsAsync(string action, string no) =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND EntityId = @n", ("@a", action), ("@t", AuditEntities.PurchaseOrder), ("@n", no));

    private Task<int> CreatedRowsAsync(string no) => RowsAsync(AuditActions.PurchaseOrderCreated, no);

    [Fact]
    public async Task A_saved_order_has_exactly_one_Created_row_that_the_subscriber_staged_for_the_event()
    {
        var no = NewNo();

        await RunAsync(async sp =>
        {
            await StageOrderAsync(sp, no);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });

        Assert.Equal(1, await OrdersAsync(no));
        Assert.Equal(1, await CreatedRowsAsync(no));
    }

    [Fact]
    public async Task When_a_later_save_of_the_transaction_fails_the_order_and_the_row_the_subscriber_staged_are_rolled_back_with_it()
    {
        var first = NewNo();
        var second = NewNo();

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                await StageOrderAsync(sp, first);
                await uow.SaveChangesAsync();                                           // the first order and its Created row are written inside the transaction
                Assert.Equal(1, await RowsInThisTransactionAsync(sp, first));           // the subscriber's row is part of the same transaction

                await StageOrderAsync(sp, second, remark: new string('x', Limits.RemarkMaxLength + 1)); // too long for the column: the database refuses the second save
                await uow.SaveChangesAsync();
                return 0;
            });
        }));

        Assert.Equal((0, 0), (await OrdersAsync(first), await CreatedRowsAsync(first)));
        Assert.Equal((0, 0), (await OrdersAsync(second), await CreatedRowsAsync(second)));
    }

    [Fact]
    public async Task A_retried_attempt_leaves_one_order_and_one_Created_row()
    {
        var no = NewNo();
        var attempts = 0;

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                attempts++;
                await StageOrderAsync(sp, no);
                await uow.SaveChangesAsync();
                if (attempts == 1) throw new ConcurrencyException(Messages.ConcurrencyConflict);   // the first attempt is rolled back whole, row included
                return 0;
            });
        });

        Assert.Equal(2, attempts);
        Assert.Equal(1, await OrdersAsync(no));
        Assert.Equal(1, await CreatedRowsAsync(no));
    }

    [Fact]
    public async Task A_cancel_that_loads_its_order_inside_a_retried_body_leaves_one_Cancelled_row_and_the_order_cancelled()
    {
        var no = NewNo();
        await RunAsync(async sp =>
        {
            await StageOrderAsync(sp, no);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });
        var attempts = 0;

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var orders = sp.GetRequiredService<IRepository<PurchaseOrder>>();
            await uow.ExecuteAsync(async () =>
            {
                attempts++;
                var order = await orders.FirstOrDefaultAsync(new PurchaseOrderForUpdateSpec(await PublicIdAsync(sp, no)), CancellationToken.None) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);   // loaded INSIDE the body, as the cancel handler does
                order.Cancel("Supplier withdrew");
                await uow.SaveChangesAsync();
                if (attempts == 1) throw new ConcurrencyException(Messages.ConcurrencyConflict);   // the first attempt is rolled back whole, with the row its event staged
                return 0;
            });
        });

        Assert.Equal(2, attempts);
        Assert.Equal(1, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PurchaseOrder WHERE No = @n AND Status = @s", ("@n", no), ("@s", (int)PurchaseOrderStatus.Cancelled)));
        Assert.Equal(1, await RowsAsync(AuditActions.PurchaseOrderCancelled, no));
    }

    private static async Task<Guid> PublicIdAsync(IServiceProvider sp, string no) =>
        await sp.GetRequiredService<AppDbContext>().Set<PurchaseOrder>().AsNoTracking().Where(o => o.No == no).Select(o => o.PublicId).SingleAsync();

    /// <summary>Counts the Created rows of the order on the connection of the open transaction: not visible to another connection until it commits.</summary>
    private static async Task<int> RowsInThisTransactionAsync(IServiceProvider sp, string no) =>
        await sp.GetRequiredService<AppDbContext>().Set<AuditLog>().CountAsync(l => l.Action == AuditActions.PurchaseOrderCreated && l.EntityId == no);
}
