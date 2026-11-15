using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Sales;

/// <summary>
/// DDD-11b against real SQL Server: the audit row that <c>SalesOrderAuditSubscriber</c> stages for an event commits or rolls back WITH the business change (AUTH-03: an audit row cannot be lost, and no row
/// describes a change that did not happen). The host is the real one, so the real subscriber runs; the unit of work is driven directly with real sales orders. The rows of every verb through the HTTP
/// pipeline are pinned by <c>SalesOrderAuditRowsTests</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SalesOrderAuditSubscriberRollbackTests(ApiFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _host = null!;
    private int _warehouseId, _customerId, _productId;

    public async Task InitializeAsync()
    {
        _host = fixture.CreateHost(_ => { });
        _ = _host.Services;

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var uom = Uom.Create(Unique.Code("U"));
        var warehouse = Warehouse.Create(Unique.Code("W"), Unique.Name("Warehouse"));
        var customer = Partner.Create(PartnerType.Customer, Unique.Name("Customer"));
        var product = Product.Create(Unique.Sku(), Unique.Name("Product"), null, uom, 0m);
        db.AddRange(uom, warehouse, customer, product);
        await db.SaveChangesAsync();
        (_warehouseId, _customerId, _productId) = (warehouse.Id, customer.Id, product.Id);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string NewNo() => $"SO-{Unique.Code("S")}";

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        using var scope = _host.Services.CreateScope();
        await body(scope.ServiceProvider);
    }

    /// <summary>A new Draft with one line, tracked as Added: raises <see cref="SalesOrderCreated"/>, which the subscriber turns into a row at the next save.</summary>
    private async Task StageOrderAsync(IServiceProvider sp, string no, string? remark = null)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var warehouse = await db.Set<Warehouse>().SingleAsync(w => w.Id == _warehouseId);
        var customer = await db.Set<Partner>().SingleAsync(c => c.Id == _customerId);
        var product = await db.Set<Product>().SingleAsync(p => p.Id == _productId);
        var order = SalesOrder.Create(warehouse, customer, remark, 1, DateTime.UtcNow);
        order.AssignNumber(no);
        order.ReplaceLines([SalesOrderLine.Create(product, 5m, null, 10m, null)]);
        sp.GetRequiredService<IRepository<SalesOrder>>().Add(order);
    }

    private Task<int> OrdersAsync(string no) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder WHERE No = @n", ("@n", no));

    private Task<int> CreatedRowsAsync(string no) =>
        fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND EntityId = @n", ("@a", AuditActions.SalesOrderCreated), ("@t", AuditEntities.SalesOrder), ("@n", no));

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

    /// <summary>Counts the Created rows of the order on the connection of the open transaction: not visible to another connection until it commits.</summary>
    private static async Task<int> RowsInThisTransactionAsync(IServiceProvider sp, string no) =>
        await sp.GetRequiredService<AppDbContext>().Set<AuditLog>().CountAsync(l => l.Action == AuditActions.SalesOrderCreated && l.EntityId == no);
}
