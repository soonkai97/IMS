using Ims.Api.Tests.Harness;
using Ims.Api.Tests.Support;
using Ims.Application;
using Ims.Domain;
using Ims.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ims.Api.Tests.Tests.Events;

/// <summary>
/// DDD-11a, the domain-event dispatcher against real SQL Server (docs/architecture/ddd-roadmap.md 2.5). Nothing in the application subscribes yet, so these tests run on a host that registers test-only
/// handlers (<see cref="EventProbe"/>) and drive the unit of work directly with real sales orders. The cases that matter for a transaction: an in-transaction handler runs inside it and rolls it back by
/// throwing; a retried attempt leaves ONE committed effect and runs the after-commit handler once; an after-commit handler that fails never fails the committed request.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DomainEventDispatchTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly EventProbe _probe = new();
    private WebApplicationFactory<Program> _host = null!;
    private int _warehouseId, _customerId, _productId;

    public async Task InitializeAsync()
    {
        _host = fixture.CreateHost(b => b.ConfigureServices(services =>
        {
            services.AddSingleton(_probe);
            services.AddScoped(typeof(IDomainEventHandler<>), typeof(ProbeHandler<>));
            services.AddScoped(typeof(IAfterCommitHandler<>), typeof(ProbeAfterCommitHandler<>));
        }));
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

    private static string NewNo() => $"SO-{Unique.Code("E")}";

    private async Task RunAsync(Func<IServiceProvider, Task> body)
    {
        using var scope = _host.Services.CreateScope();
        await body(scope.ServiceProvider);
    }

    private static async Task<Product> LoadProductAsync(IServiceProvider sp, int id) => await sp.GetRequiredService<AppDbContext>().Set<Product>().SingleAsync(p => p.Id == id);

    private static SalesOrderLine OneLine(Product product) => SalesOrderLine.Create(product, 5m, null, 10m, null);

    /// <summary>A new Draft with one line, tracked as Added: raises <see cref="SalesOrderCreated"/>. Its row is written by the next save.</summary>
    private async Task<SalesOrder> StageOrderAsync(IServiceProvider sp, string no, bool track = true, string? remark = null)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var warehouse = await db.Set<Warehouse>().SingleAsync(w => w.Id == _warehouseId);
        var customer = await db.Set<Partner>().SingleAsync(c => c.Id == _customerId);
        var order = SalesOrder.Create(warehouse, customer, remark, 1, DateTime.UtcNow);
        order.AssignNumber(no);
        order.ReplaceLines([OneLine(await LoadProductAsync(sp, _productId))]);
        if (track) Track(sp, order);
        return order;
    }

    private static void Track(IServiceProvider sp, SalesOrder order) => sp.GetRequiredService<IRepository<SalesOrder>>().Add(order);

    private static void Submit(SalesOrder order) => order.Submit(1, requiresApproval: false, isLarge: false, DateTime.UtcNow);

    private static void Stage(IServiceProvider sp, string marker) => sp.GetRequiredService<IAuditTrail>().Record("probe.handled", "Probe", marker);

    private Task<int> OrdersAsync(string no) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder WHERE No = @n", ("@n", no));

    private Task<int> ProbeRowsAsync(string marker) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM AuditLogs WHERE Action = 'probe.handled' AND EntityId = @m", ("@m", marker));

    private static string Describe(IDomainEvent e) => e switch
    {
        SalesOrderCreated c => $"created {c.No}",
        SalesOrderSubmitted s => $"submitted {s.No}",
        SalesOrderUpdated u => $"updated {u.No}",
        _ => e.GetType().Name,
    };

    // (a)
    [Fact]
    public async Task An_event_raised_by_a_tracked_aggregate_reaches_its_handler_exactly_once_inside_the_transaction()
    {
        var no = NewNo();
        var insideTransaction = new List<bool>();
        _probe.OnInTransaction = (_, sp) =>
        {
            insideTransaction.Add(sp.GetRequiredService<AppDbContext>().Database.CurrentTransaction is not null);
            return Task.CompletedTask;
        };

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                await StageOrderAsync(sp, no);
                await uow.SaveChangesAsync();
                await uow.SaveChangesAsync(); // a second save finds nothing new: the events were taken and cleared
                return 0;
            });
        });

        var created = Assert.Single(_probe.InTransaction.OfType<SalesOrderCreated>());
        Assert.Equal(no, created.No);
        Assert.Equal([true], insideTransaction);
        Assert.Equal(1, await OrdersAsync(no));
    }

    // (b)
    [Fact]
    public async Task A_handler_that_throws_rolls_back_the_business_rows_and_everything_it_staged()
    {
        var no = NewNo();
        var warehouseCode = Unique.Code("RB");
        _probe.OnInTransaction = (_, sp) =>
        {
            Stage(sp, no);
            throw new InvalidOperationException("the handler failed");
        };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                sp.GetRequiredService<IRepository<Warehouse>>().Add(Warehouse.Create(warehouseCode, Unique.Name("Rolled back")));
                await uow.SaveChangesAsync();   // a business row, saved inside the transaction
                await StageOrderAsync(sp, no);
                await uow.SaveChangesAsync();   // the handler runs here and throws
                return 0;
            });
        }));

        Assert.Equal("the handler failed", thrown.Message);
        Assert.Equal(0, await fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM Warehouse WHERE Code = @c", ("@c", warehouseCode)));
        Assert.Equal(0, await OrdersAsync(no));
        Assert.Equal(0, await ProbeRowsAsync(no));
        Assert.Empty(_probe.AfterCommit);
    }

    // (c)
    [Fact]
    public async Task A_retried_attempt_leaves_one_committed_effect_and_runs_the_after_commit_handler_once()
    {
        var no = NewNo();
        var attempts = 0;
        var attemptAtAfterCommit = new List<int>();
        _probe.OnInTransaction = (_, sp) => { Stage(sp, no); return Task.CompletedTask; };
        _probe.OnAfterCommit = (_, _) => { attemptAtAfterCommit.Add(attempts); return Task.CompletedTask; };

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                attempts++;
                await StageOrderAsync(sp, no);
                await uow.SaveChangesAsync();
                if (attempts == 1) throw new ConcurrencyException(Messages.ConcurrencyConflict);
                return 0;
            });
        });

        Assert.Equal(2, attempts);
        Assert.Equal(1, await OrdersAsync(no));
        Assert.Equal(1, await ProbeRowsAsync(no));                  // the in-transaction effect of the discarded attempt rolled back with it
        Assert.Equal(2, _probe.InTransaction.OfType<SalesOrderCreated>().Count(e => e.No == no)); // it ran in both attempts
        Assert.Equal([2], attemptAtAfterCommit);                    // nothing after commit for the discarded attempt; once overall
    }

    // (d)
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_after_commit_handler_that_fails_never_fails_the_committed_request_or_runs_the_work_twice(bool retriable)
    {
        var no = NewNo();
        var workRuns = 0;
        _probe.OnAfterCommit = (_, _) => throw (retriable ? new ConcurrencyException(Messages.ConcurrencyConflict) : new InvalidOperationException("the notification failed"));

        var result = 0;
        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            result = await uow.ExecuteAsync(async () =>
            {
                workRuns++;
                await StageOrderAsync(sp, no);
                await uow.SaveChangesAsync();
                return 42;
            });
        });

        Assert.Equal(42, result);
        Assert.Equal(1, workRuns);
        Assert.Equal(1, await OrdersAsync(no));
        Assert.Single(_probe.AfterCommit.OfType<SalesOrderCreated>());
    }

    // (e)
    [Fact]
    public async Task Events_of_two_aggregates_are_delivered_in_the_order_the_domain_raised_them_not_in_tracking_order()
    {
        var (first, second) = (NewNo(), NewNo());

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                var a = await StageOrderAsync(sp, first, track: false);    // raises A created (1)
                var b = await StageOrderAsync(sp, second, track: false);   // raises B created (2)
                Submit(a);                                                 // raises A submitted (3)
                Track(sp, b);                                              // the tracker holds B first, then A
                Track(sp, a);
                await uow.SaveChangesAsync();
                return a.No;
            });
        });

        Assert.Equal([$"created {first}", $"created {second}", $"submitted {first}"], _probe.InTransaction.Select(Describe).ToList());
    }

    [Fact]
    public async Task An_event_raised_by_a_handler_is_delivered_in_the_next_round_after_the_rest_of_this_one()
    {
        var (first, second) = (NewNo(), NewNo());
        SalesOrder? a = null;
        _probe.OnInTransaction = (e, _) =>
        {
            if (e is SalesOrderCreated c && c.No == first) Submit(a!);   // raised while round 1 is running
            return Task.CompletedTask;
        };

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                a = await StageOrderAsync(sp, first);
                await StageOrderAsync(sp, second);
                await uow.SaveChangesAsync();
                return 0;
            });
        });

        Assert.Equal([$"created {first}", $"created {second}", $"submitted {first}"], _probe.InTransaction.Select(Describe).ToList()); // not between the two creations
        Assert.Equal(1, await OrdersAsync(first));
    }

    [Fact]
    public async Task Handlers_that_keep_raising_events_stop_after_100_rounds_and_roll_the_transaction_back()
    {
        var no = NewNo();
        SalesOrder? order = null;
        Product? product = null;
        _probe.OnInTransaction = (_, _) =>
        {
            order!.ReplaceLines([OneLine(product!)]);   // an update raises Updated, which runs this handler again
            return Task.CompletedTask;
        };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                product = await LoadProductAsync(sp, _productId);
                order = await StageOrderAsync(sp, no);
                await uow.SaveChangesAsync();
                return 0;
            });
        }));

        Assert.Contains("100", thrown.Message);
        Assert.Equal(100, _probe.InTransaction.Count);
        Assert.Equal(0, await OrdersAsync(no));
    }

    // (f)
    [Fact]
    public async Task A_handler_that_saves_throws()
    {
        var no = NewNo();
        _probe.OnInTransaction = async (_, sp) => await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                await StageOrderAsync(sp, no);
                await uow.SaveChangesAsync();
                return 0;
            });
        }));

        Assert.Equal(0, await OrdersAsync(no));
    }

    // (g)
    [Fact]
    public async Task A_bare_save_with_no_transaction_runs_the_after_commit_handlers_once_the_rows_are_committed()
    {
        var no = NewNo();
        var rowsSeenAfterCommit = new List<int>();
        _probe.OnAfterCommit = async (_, _) => rowsSeenAfterCommit.Add(await OrdersAsync(no));

        await RunAsync(async sp =>
        {
            await StageOrderAsync(sp, no);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });

        Assert.Single(_probe.AfterCommit.OfType<SalesOrderCreated>());
        Assert.Equal([1], rowsSeenAfterCommit);   // read from another connection: the transaction was already committed
    }

    [Fact]
    public async Task Inside_a_transaction_only_the_owner_flushes_after_commit_not_a_save_and_not_a_nested_scope()
    {
        var (first, second) = (NewNo(), NewNo());
        var deliveredBeforeCommit = -1;

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                await StageOrderAsync(sp, first);
                await uow.SaveChangesAsync();
                await uow.ExecuteAsync(async () =>   // joins the outer transaction: the owner flushes
                {
                    await StageOrderAsync(sp, second);
                    await uow.SaveChangesAsync();
                    return 0;
                });
                deliveredBeforeCommit = _probe.AfterCommit.Count;
                return 0;
            });
        });

        Assert.Equal(0, deliveredBeforeCommit);
        Assert.Equal([first, second], _probe.AfterCommit.OfType<SalesOrderCreated>().Select(e => e.No).ToList());
    }

    [Fact]
    public async Task A_failed_use_case_delivers_nothing_after_commit()
    {
        var no = NewNo();

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync<int>(async () =>
            {
                await StageOrderAsync(sp, no);
                await uow.SaveChangesAsync();
                throw new InvalidOperationException("the use case failed after its save");
            });
        }));

        Assert.Equal(0, await OrdersAsync(no));
        Assert.Empty(_probe.AfterCommit);
    }

    // ----- review of PR 284 -----

    /// <summary>A Draft with one line that is already committed; what the probe saw while it was created is forgotten.</summary>
    private async Task<string> CommittedOrderAsync()
    {
        var no = NewNo();
        await RunAsync(async sp =>
        {
            await StageOrderAsync(sp, no);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        });
        _probe.InTransaction.Clear();
        _probe.AfterCommit.Clear();
        return no;
    }

    private static async Task<SalesOrder> LoadOrderAsync(IServiceProvider sp, string no) =>
        await sp.GetRequiredService<AppDbContext>().Set<SalesOrder>().Include(o => o.Lines).SingleAsync(o => o.No == no);

    private Task<int> SubmittedAsync(string no) => fixture.Database.ScalarAsync<int>("SELECT COUNT(*) FROM SalesOrder WHERE No = @n AND SubmittedBy IS NOT NULL", ("@n", no));

    private Task ChangeBehindTheBackAsync(string no) => fixture.Database.ExecuteAsync("UPDATE SalesOrder SET Remark = 'changed by another request' WHERE No = @n", ("@n", no));

    [Fact]
    public async Task A_handler_that_runs_a_transaction_of_its_own_throws_instead_of_dropping_the_queued_after_commit_items()
    {
        var (first, second) = (NewNo(), NewNo());
        _probe.OnInTransaction = async (e, sp) =>
        {
            if (e is SalesOrderCreated c && c.No == second) await sp.GetRequiredService<IUnitOfWork>().ExecuteAsync(() => Task.FromResult(0));
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(async sp =>
        {
            await StageOrderAsync(sp, first);
            await StageOrderAsync(sp, second);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();   // a bare save: the handler of the second event asks for a transaction
        }));

        Assert.Equal(0, await OrdersAsync(first));
        Assert.Equal(0, await OrdersAsync(second));
        Assert.Empty(_probe.AfterCommit);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_save_that_failed_inside_a_transaction_queues_nothing_for_after_commit_even_if_the_body_goes_on_and_commits(bool databaseRejects)
    {
        var (first, second, good) = (NewNo(), NewNo(), NewNo());
        if (!databaseRejects) _probe.OnInTransaction = (e, _) => e is SalesOrderCreated c && c.No == second ? throw new InvalidOperationException("the handler failed") : Task.CompletedTask;

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                await StageOrderAsync(sp, first);
                await StageOrderAsync(sp, second, remark: databaseRejects ? new string('x', 5000) : null);   // too long for the column: the database refuses the save
                try
                {
                    await uow.SaveChangesAsync();
                }
                catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException)
                {
                    sp.GetRequiredService<AppDbContext>().ChangeTracker.Clear();   // the body gives up on those two orders and goes on
                }
                await StageOrderAsync(sp, good);
                await uow.SaveChangesAsync();
                return 0;
            });
        });

        Assert.Equal([good], _probe.AfterCommit.OfType<SalesOrderCreated>().Select(e => e.No).ToList());   // nothing for the orders that were never written
        Assert.Equal(1, await OrdersAsync(good));
        Assert.Equal(0, await OrdersAsync(first));
        Assert.Equal(0, await OrdersAsync(second));
    }

    [Fact]
    public async Task A_bare_save_that_loses_a_concurrency_race_after_the_events_were_taken_delivers_nothing_after_commit()
    {
        var no = await CommittedOrderAsync();
        SalesOrder? order = null;

        await Assert.ThrowsAsync<ConcurrencyException>(() => RunAsync(async sp =>
        {
            order = await LoadOrderAsync(sp, no);
            await ChangeBehindTheBackAsync(no);
            Submit(order);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }));

        Assert.Equal(["SalesOrderSubmitted"], _probe.InTransaction.Select(e => e.GetType().Name).ToList());   // the in-transaction handler saw it, inside the save that then failed
        Assert.Empty(_probe.AfterCommit);                                                                      // the save never committed
        Assert.Empty(order!.DomainEvents);                                                                     // taken: the aggregate keeps nothing
        Assert.Equal(0, await SubmittedAsync(no));
    }

    [Fact]
    public async Task A_use_case_that_loses_a_concurrency_race_is_retried_and_delivers_after_commit_once()
    {
        var no = await CommittedOrderAsync();
        var attempts = 0;

        await RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                attempts++;
                var order = await LoadOrderAsync(sp, no);   // loaded inside the body, so every attempt starts from the database
                if (attempts == 1) await ChangeBehindTheBackAsync(no);
                Submit(order);
                await uow.SaveChangesAsync();
                return 0;
            });
        });

        Assert.Equal(2, attempts);
        Assert.Equal(2, _probe.InTransaction.OfType<SalesOrderSubmitted>().Count());
        Assert.Single(_probe.AfterCommit.OfType<SalesOrderSubmitted>());
        Assert.Equal(1, await SubmittedAsync(no));
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_dispatch_before_the_next_event_and_nothing_is_written_or_delivered()
    {
        var (first, second) = (NewNo(), NewNo());
        using var cancellation = new CancellationTokenSource();
        _probe.OnInTransaction = (_, _) =>
        {
            cancellation.Cancel();
            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(async sp =>
        {
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await uow.ExecuteAsync(async () =>
            {
                await StageOrderAsync(sp, first);
                await StageOrderAsync(sp, second);
                await uow.SaveChangesAsync(cancellation.Token);
                return 0;
            }, cancellation.Token);
        }));

        Assert.Single(_probe.InTransaction);   // the handler of the second event never ran
        Assert.Empty(_probe.AfterCommit);
        Assert.Equal(0, await OrdersAsync(first));
        Assert.Equal(0, await OrdersAsync(second));
    }
}
