using System.Globalization;
using Ardalis.Specification;
using Ims.Application;
using Ims.Application.Features.Audit;
using Ims.Application.Features.Sales;
using Ims.Domain.Tests.Stock;
using Microsoft.Extensions.Logging.Abstractions;
using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Sales;

/// <summary>
/// DDD-11b: <see cref="SalesOrderAuditSubscriber"/> turns each of the eight sales-order events into the audit row the handlers wrote before (docs/architecture/ddd-roadmap.md 2.4, 2.5). It reads only the facts the
/// event carries. The rows go through the real <see cref="AuditTrail"/>, so what is compared is the exact row: action, entity type, entity id, actor, time and the raw JSON of Before and After (key order, enum names,
/// nulls and the scale of an amount included). <c>SalesOrderAuditRowsTests</c> (Api) proves the same rows through the whole stack.
/// </summary>
public class SalesOrderAuditSubscriberTests
{
    private const string No = "SO-WH1-2601-0001";
    private const string Who = "clerk";

    private sealed record Row(string Actor, string Action, string EntityType, string EntityId, string? Before, string? After, DateTime At);

    private sealed class Logs : IRepository<AuditLog>
    {
        public List<AuditLog> Added { get; } = [];

        public void Add(AuditLog entity) => Added.Add(entity);
        public void AddRange(IEnumerable<AuditLog> entities) => Added.AddRange(entities);
        public void Remove(AuditLog entity) => throw new NotSupportedException();
        public void RemoveRange(IEnumerable<AuditLog> entities) => throw new NotSupportedException();
        public Task<AuditLog?> FirstOrDefaultAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<AuditLog>> ListAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<AuditLog>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<TResult>> ListAsync<TResult>(ISpecification<AuditLog, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuditLog?> GetByIdAsync<TId>(TId id, CancellationToken cancellationToken = default) where TId : notnull => throw new NotSupportedException();
        public Task<TResult?> FirstOrDefaultAsync<TResult>(ISpecification<AuditLog, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AuditLog?> SingleOrDefaultAsync(ISingleResultSpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResult?> SingleOrDefaultAsync<TResult>(ISingleResultSpecification<AuditLog, TResult> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(ISpecification<AuditLog> specification, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> AnyAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<AuditLog> AsAsyncEnumerable(ISpecification<AuditLog> specification) => throw new NotSupportedException();
    }

    private sealed class Caller : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public int UserId => 1;
        public string Username => Who;
        public string Role => throw new NotSupportedException();
        public IReadOnlyCollection<int> WarehouseIds => throw new NotSupportedException();
        public bool CanAccessWarehouse(int warehouseId) => throw new NotSupportedException();
        public bool AllWarehouses => true;
        public ActorRights Rights() => throw new NotSupportedException();
    }

    private readonly Logs _logs = new();

    private async Task<List<Row>> HandleAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        var subscriber = new SalesOrderAuditSubscriber(new AuditTrail(_logs, new Caller(), new FakeClock(), NullLogger<AuditTrail>.Instance));
        await ((IDomainEventHandler<TEvent>)(object)subscriber).HandleAsync(domainEvent, CancellationToken.None);
        return _logs.Added.Select(l => new Row(l.Actor, l.Action, l.EntityType, l.EntityId, l.Before, l.After, l.At)).ToList();
    }

    private static Row Expected(string action, string? before, string? after) => new(Who, action, AuditEntities.SalesOrder, No, before, after, Now);

    private static decimal D(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture); // keeps the scale the text has: "12.50" is 12.50, not 12.5

    private static string Json(bool value) => value ? "true" : "false";

    private static SalesOrder DraftWithOneLine(decimal price)
    {
        var order = SalesOrder.Create(NewWarehouse(), NewCustomer(), null, 11, Now);
        order.AssignNumber(No);
        order.ReplaceLines([SalesOrderLine.Create(NewProduct(1, "SKU-1"), 1m, null, price, null)]);
        return order;
    }

    private static string Summary(string status, int lines, string amount) => $$"""{"Status":"{{status}}","WarehouseId":1,"CustomerId":200,"Lines":{{lines}},"SellingAmount":{{amount}}}""";

    // ----- created and updated: the amount keeps the scale it was computed in (the audit row prints a decimal as it is, so 12, 12.5 and 12.50 are three different rows) -----
    [Theory]
    [InlineData("12", "12")]
    [InlineData("12.5", "12.5")]
    [InlineData("12.50", "12.50")]
    [InlineData("2.50", "2.50")]
    public async Task Created_writes_one_row_with_no_before_and_the_summary_with_the_amount_at_the_scale_it_was_computed_in(string price, string amountAsWritten)
    {
        var created = Assert.IsType<SalesOrderCreated>(Assert.Single(DraftWithOneLine(D(price)).DomainEvents));

        var rows = await HandleAsync(created);

        Assert.Equal([Expected("sales-order.created", null, Summary("Draft", 1, amountAsWritten))], rows);
    }

    [Fact]
    public async Task Updated_writes_one_row_with_the_before_summary_as_it_was_and_the_after_summary_as_computed()
    {
        var order = DraftWithOneLine(25.00m);
        order.ClearDomainEvents();
        order.UpdateHeader(NewCustomer(), "r");
        order.ReplaceLines([SalesOrderLine.Create(NewProduct(1, "SKU-1"), 1m, null, 12.5m, null), SalesOrderLine.Create(NewProduct(2, "SKU-2"), 2m, null, 1.50m, null)]);
        var updated = Assert.IsType<SalesOrderUpdated>(Assert.Single(order.DomainEvents));

        var rows = await HandleAsync(updated);

        Assert.Equal([Expected("sales-order.updated", Summary("Draft", 1, "25.00"), Summary("Draft", 2, "15.50"))], rows);
    }

    // ----- the workflow verbs, from the facts of the event -----
    [Theory]
    [InlineData(false, false, "Approved", "100.00")]
    [InlineData(true, false, "Submitted", "6000.00")]
    [InlineData(true, true, "Submitted", "25000.00")]
    public async Task Submitted_writes_the_status_before_and_the_status_the_approval_flags_and_the_amount(bool requires, bool large, string status, string amount)
    {
        var rows = await HandleAsync(new SalesOrderSubmitted(No, SalesOrderStatus.Draft, Enum.Parse<SalesOrderStatus>(status), requires, large, Money.Rounded(D(amount))));

        Assert.Equal(
            [Expected("sales-order.submitted", """{"Status":"Draft"}""",
                $$"""{"Status":"{{status}}","RequiresApproval":{{Json(requires)}},"IsLarge":{{Json(large)}},"SellingAmount":{{amount}}}""")],
            rows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Approved_writes_Submitted_before_and_the_status_and_IsLarge_after(bool large)
    {
        var rows = await HandleAsync(new SalesOrderApproved(No, SalesOrderStatus.Submitted, SalesOrderStatus.Approved, large));

        Assert.Equal([Expected("sales-order.approved", """{"Status":"Submitted"}""", $$"""{"Status":"Approved","IsLarge":{{Json(large)}}}""")], rows);
    }

    [Fact]
    public async Task Rejected_writes_the_status_change_and_the_reason()
    {
        var rows = await HandleAsync(new SalesOrderRejected(No, SalesOrderStatus.Submitted, SalesOrderStatus.Draft, "Price too high"));

        Assert.Equal([Expected("sales-order.rejected", """{"Status":"Submitted"}""", """{"Status":"Draft","Reason":"Price too high"}""")], rows);
    }

    [Fact]
    public async Task Recalled_writes_the_status_and_the_approval_flag_before_and_the_status_after()
    {
        var rows = await HandleAsync(new SalesOrderRecalled(No, SalesOrderStatus.Submitted, RequiresApprovalBefore: true, SalesOrderStatus.Draft));

        Assert.Equal([Expected("sales-order.recalled", """{"Status":"Submitted","RequiresApproval":true}""", """{"Status":"Draft"}""")], rows);
    }

    [Theory]
    [InlineData(SalesOrderStatus.Draft, null, """{"Status":"Cancelled","Reason":null}""")]
    [InlineData(SalesOrderStatus.Approved, "Customer withdrew the order", """{"Status":"Cancelled","Reason":"Customer withdrew the order"}""")]
    public async Task Cancelled_writes_the_status_change_and_the_reason_or_null(SalesOrderStatus before, string? reason, string after)
    {
        var rows = await HandleAsync(new SalesOrderCancelled(No, before, SalesOrderStatus.Cancelled, reason));

        Assert.Equal([Expected("sales-order.cancelled", $$"""{"Status":"{{before}}"}""", after)], rows);
    }

    [Fact]
    public async Task Closed_short_writes_the_status_change_the_kind_and_the_reason()
    {
        var rows = await HandleAsync(new SalesOrderClosed(No, SalesOrderStatus.Approved, SalesOrderStatus.Closed, SalesOrderCloseKind.Short, "Customer will take no more", null));

        Assert.Equal([Expected("sales-order.closed", """{"Status":"Approved"}""", """{"Status":"Closed","CloseKind":"Short","Reason":"Customer will take no more"}""")], rows);
    }

    [Fact]
    public async Task Closed_as_delivered_writes_nothing_because_posting_the_delivery_still_writes_that_row_itself()
    {
        var rows = await HandleAsync(new SalesOrderClosed(No, SalesOrderStatus.Approved, SalesOrderStatus.Closed, SalesOrderCloseKind.Delivered, null, "DEL-WH1-2601-0001"));

        Assert.Empty(rows);
    }

    [Fact]
    public async Task The_actor_is_the_current_user_and_the_time_is_the_clock_not_the_event()
    {
        var rows = await HandleAsync(new SalesOrderApproved(No, SalesOrderStatus.Submitted, SalesOrderStatus.Approved, false));

        var row = Assert.Single(rows);
        Assert.Equal((Who, Now), (row.Actor, row.At));
    }
}
