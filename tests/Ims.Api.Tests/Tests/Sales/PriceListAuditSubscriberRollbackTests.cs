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
/// DDD-13 against real SQL Server: the audit row that <c>PriceListAuditSubscriber</c> stages for an event commits or rolls back WITH the business change (AUTH-03: an audit row cannot be lost, and no row
/// describes a change that did not happen). The host is the real one, so the real subscriber and the real label lookup run; the unit of work is driven directly with real price lists. The rows of every verb
/// through the HTTP pipeline are pinned by <c>PriceListAuditRowsTests</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PriceListAuditSubscriberRollbackTests(ApiFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _host = null!;
    private int _productId;
    private string _sku = "";
    private string _uomCode = "";

    public async Task InitializeAsync()
    {
        _host = fixture.CreateHost(_ => { });
        _ = _host.Services;

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _uomCode = Unique.Code("U");
        _sku = Unique.Sku("RB");
        var uom = Uom.Create(_uomCode);
        var product = Product.Create(_sku, Unique.Name("Product"), null, uom, 0m);
        db.AddRange(uom, product);
        await db.SaveChangesAsync();
        _productId = product.Id;
    }

    public async Task DisposeAsync()
    {
        await _host.DisposeAsync();
        await fixture.Database.ExecuteAsync("DELETE FROM PriceListItem");   // the shared database keeps one default and one list per customer: a test leaves none
        await fixture.Database.ExecuteAsync("DELETE FROM PriceList");
    }

    private static string NewName() => Unique.Name("Rollback");

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        using var scope = _host.Services.CreateScope();
        await body(scope.ServiceProvider);
    }

    /// <summary>A new list with one priced item, tracked as Added: raises <see cref="PriceListCreated"/>, which the subscriber turns into a row at the next save.</summary>
    private async Task StageListAsync(IServiceProvider sp, string name)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var product = await db.Set<Product>().Include(p => p.Conversions).SingleAsync(p => p.Id == _productId);
        var list = PriceList.Create(name, false, null);
        list.ReplaceItems([PriceListItem.Create(product, null, 3m)]);
        sp.GetRequiredService<IRepository<PriceList>>().Add(list);
    }

    private Task<int> ListsAsync(string name) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM PriceList WHERE Name = @n", ("@n", name));

    /// <summary>The Created rows of the list of that name (the entity id is the public id of the list).</summary>
    private Task<int> CreatedRowsAsync(string name) => fixture.Database.ScalarAsync<int>(
        "SELECT COUNT(*) FROM AuditLogs a JOIN PriceList l ON CONVERT(nvarchar(36), l.PublicId) = a.EntityId WHERE a.Action = @a AND a.EntityType = @t AND l.Name = @n",
        ("@a", AuditActions.PriceListCreated), ("@t", AuditEntities.PriceList), ("@n", name));

    /// <summary>The Created rows that name this test's product and unit in words: they exist only when the subscriber ran and its lookup found both.</summary>
    private Task<int> NamedRowsAsync() => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = @a AND EntityType = @t AND [After] LIKE @words",
        ("@a", AuditActions.PriceListCreated), ("@t", AuditEntities.PriceList), ("@words", $"%{_sku} ({_uomCode}): added at 3.00%"));

    [Fact]
    public async Task A_saved_list_has_exactly_one_Created_row_that_the_subscriber_staged_for_the_event_with_the_item_named_by_the_lookup()
    {
        var name = NewName();

        await RunAsync(async sp =>
        {
            await StageListAsync(sp, name);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });

        Assert.Equal(1, await ListsAsync(name));
        Assert.Equal(1, await CreatedRowsAsync(name));
        Assert.Equal(1, await NamedRowsAsync());   // the row names the product and the unit: the lookup ran
    }

    [Fact]
    public async Task When_a_later_save_of_the_transaction_fails_the_list_and_the_row_the_subscriber_staged_are_rolled_back_with_it()
    {
        var name = NewName();

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                await StageListAsync(sp, name);
                await uow.SaveChangesAsync();                                           // the list and its Created row are written inside the transaction
                Assert.Equal(1, await RowsInThisTransactionAsync(sp));                  // the subscriber's row is part of the same transaction

                sp.GetRequiredService<IAuditTrail>().Record(new string('x', Limits.AuditTextMaxLength + 1), AuditEntities.PriceList, "x");  // too long for the column: the database refuses the second save
                await uow.SaveChangesAsync();
                return 0;
            });
        }));

        Assert.Equal((0, 0), (await ListsAsync(name), await CreatedRowsAsync(name)));
        Assert.Equal(0, await NamedRowsAsync());
    }

    [Fact]
    public async Task A_retried_attempt_leaves_one_list_and_one_Created_row()
    {
        var name = NewName();
        var attempts = 0;

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                attempts++;
                await StageListAsync(sp, name);   // built INSIDE the body: an aggregate built before it would lose its events when the body retries
                await uow.SaveChangesAsync();
                if (attempts == 1) throw new ConcurrencyException(Messages.ConcurrencyConflict);   // the first attempt is rolled back whole, row included
                return 0;
            });
        });

        Assert.Equal(2, attempts);
        Assert.Equal(1, await ListsAsync(name));
        Assert.Equal(1, await CreatedRowsAsync(name));
        Assert.Equal(1, await NamedRowsAsync());
    }

    /// <summary>Counts the Created rows that name the item on the connection of the open transaction: not visible to another connection until it commits.</summary>
    private async Task<int> RowsInThisTransactionAsync(IServiceProvider sp) =>
        await sp.GetRequiredService<AppDbContext>().Set<AuditLog>().CountAsync(l => l.Action == AuditActions.PriceListCreated && l.After!.Contains($"{_sku} ({_uomCode}): added at 3.00"));
}
