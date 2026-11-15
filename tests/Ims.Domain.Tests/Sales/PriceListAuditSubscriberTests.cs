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
/// DDD-13: <see cref="PriceListAuditSubscriber"/> turns each of the four price-list events into the audit row the handlers wrote before (docs/architecture/ddd-roadmap.md 2.4, 2.5). It reads only the facts the
/// event carries; the SKU and the unit code of each priced item come from ONE lookup per event (<see cref="IPriceListLabelReader"/>), asked only for the items the row names. The rows go through the real
/// <see cref="AuditTrail"/>, so what is compared is the exact row: action, entity type, entity id, actor, time and the raw JSON of Before and After (key order, the words of the price changes and the cap of 50
/// included). <c>PriceListAuditRowsTests</c> (Api) proves the same rows through the whole stack.
/// </summary>
public class PriceListAuditSubscriberTests
{
    private const string Who = "admin";
    private static readonly Guid List = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

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

    /// <summary>The lookup: knows products 1 to 99 as "SKU-n" and the units 1 and 2 as PCS and CTN; remembers every call.</summary>
    private sealed class Labels : IPriceListLabelReader
    {
        public List<(int[] Products, int[] Uoms)> Calls { get; } = [];

        public Task<PriceListLabels> ReadAsync(IReadOnlyCollection<int> productIds, IReadOnlyCollection<int> uomIds, CancellationToken ct)
        {
            Calls.Add(([.. productIds], [.. uomIds]));
            return Task.FromResult(new PriceListLabels(
                productIds.Where(i => i is >= 1 and <= 99).ToDictionary(i => i, i => $"SKU-{i}"),
                uomIds.Where(i => i is 1 or 2).ToDictionary(i => i, i => i == 1 ? "PCS" : "CTN")));
        }
    }

    private readonly Logs _logs = new();
    private readonly Labels _labels = new();

    private async Task<List<Row>> HandleAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        var subscriber = new PriceListAuditSubscriber(new AuditTrail(_logs, new Caller(), new FakeClock(), NullLogger<AuditTrail>.Instance), _labels);
        await ((IDomainEventHandler<TEvent>)(object)subscriber).HandleAsync(domainEvent, CancellationToken.None);
        return _logs.Added.Select(l => new Row(l.Actor, l.Action, l.EntityType, l.EntityId, l.Before, l.After, l.At)).ToList();
    }

    private static Row Expected(string action, string? before, string? after) => new(Who, action, AuditEntities.PriceList, List.ToString(), before, after, Now);

    private static Money M(decimal value) => Money.Rounded(value);

    private static decimal D(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture); // keeps the scale the text has: "3.50" is 3.50, not 3.5

    private static PriceChange Added(int product, decimal price, int uom = PcsId) => new(PriceChangeKind.Added, product, uom, null, M(price));

    private static PriceChange Removed(int product, decimal price, int uom = PcsId) => new(PriceChangeKind.Removed, product, uom, M(price), null);

    private static PriceChange Repriced(int product, decimal was, decimal now, int uom = PcsId) => new(PriceChangeKind.Repriced, product, uom, M(was), M(now));

    private static string Header(string name, bool isDefault, int? customer, int items, params string[] words) =>
        $$"""{"Name":"{{name}}","IsDefault":{{(isDefault ? "true" : "false")}},"CustomerId":{{customer?.ToString() ?? "null"}},"Items":{{items}}""" +
        (words.Length == 0 ? "" : ",\"ListPriceChanges\":[" + string.Join(",", words.Select(w => $"\"{w}\"")) + "]") + "}";

    // ----- created -----
    [Fact]
    public async Task Created_writes_one_row_with_no_before_the_header_and_every_price_in_words_in_the_order_given_from_one_lookup()
    {
        var created = new PriceListCreated(List, new PriceListSummary("Wholesale", true, null, 3), [Added(3, 3m), Added(1, 12.5m), Added(2, 30m, CartonId)]);

        var rows = await HandleAsync(created);

        Assert.Equal([Expected("price-list.created", null, Header("Wholesale", true, null, 3, "SKU-3 (PCS): added at 3.00", "SKU-1 (PCS): added at 12.50", "SKU-2 (CTN): added at 30.00"))], rows);
        var call = Assert.Single(_labels.Calls);
        Assert.Equal(([3, 1, 2], [PcsId, CartonId]), (call.Products, call.Uoms));
    }

    [Fact]
    public async Task Created_with_no_items_writes_the_header_with_no_price_words_and_asks_for_no_labels()
    {
        var rows = await HandleAsync(new PriceListCreated(List, new PriceListSummary("Empty", false, 7, 0), []));

        Assert.Equal([Expected("price-list.created", null, Header("Empty", false, 7, 0))], rows);
        Assert.Empty(_labels.Calls);
    }

    [Theory]
    [InlineData("3", "3.00")]
    [InlineData("3.5", "3.50")]
    [InlineData("0.01", "0.01")]
    [InlineData("12345.67", "12345.67")]
    public async Task A_price_is_written_with_two_places_whatever_scale_it_has(string price, string written)
    {
        var rows = await HandleAsync(new PriceListCreated(List, new PriceListSummary("x", false, null, 1), [Added(1, D(price))]));

        Assert.Equal([Expected("price-list.created", null, Header("x", false, null, 1, $"SKU-1 (PCS): added at {written}"))], rows);
    }

    [Fact]
    public async Task An_item_the_lookup_cannot_name_is_written_with_a_blank_name_as_before()
    {
        var rows = await HandleAsync(new PriceListCreated(List, new PriceListSummary("x", false, null, 1), [Added(500, 1m, uom: 9)]));

        Assert.Equal([Expected("price-list.created", null, Header("x", false, null, 1, " (): added at 1.00"))], rows);
    }

    // ----- updated -----
    [Fact]
    public async Task Updated_writes_the_header_before_and_after_and_the_prices_added_then_removed_then_changed()
    {
        var updated = new PriceListUpdated(List, new PriceListSummary("Before", false, null, 4), new PriceListSummary("After", true, null, 4),
            [Added(5, 9m), Added(6, 10.5m), Removed(3, 4m), Removed(4, 5.5m), Repriced(2, 3.00m, 3.50m)]);

        var rows = await HandleAsync(updated);

        Assert.Equal(
            [Expected("price-list.updated", Header("Before", false, null, 4),
                Header("After", true, null, 4, "SKU-5 (PCS): added at 9.00", "SKU-6 (PCS): added at 10.50", "SKU-3 (PCS): removed (was 4.00)", "SKU-4 (PCS): removed (was 5.50)", "SKU-2 (PCS): changed from 3.00 to 3.50"))],
            rows);
        Assert.Single(_labels.Calls);
    }

    [Fact]
    public async Task Updated_names_the_unit_of_a_price_per_carton()
    {
        var rows = await HandleAsync(new PriceListUpdated(List, new PriceListSummary("x", false, null, 1), new PriceListSummary("x", false, null, 1), [Repriced(1, 30m, 31m, CartonId)]));

        Assert.Contains("SKU-1 (CTN): changed from 30.00 to 31.00", Assert.Single(rows).After);
    }

    [Fact]
    public async Task Updated_with_no_changes_writes_the_same_header_twice_with_no_price_words_and_asks_for_no_labels()
    {
        var header = new PriceListSummary("Same", false, 7, 2);

        var rows = await HandleAsync(new PriceListUpdated(List, header, header, []));

        Assert.Equal([Expected("price-list.updated", Header("Same", false, 7, 2), Header("Same", false, 7, 2))], rows);
        Assert.Empty(_labels.Calls);
    }

    // ----- the cap of 50 lines, and the lookup that follows it -----
    [Fact]
    public async Task Sixty_changes_are_fifty_words_and_a_count_and_the_lookup_asks_only_for_the_fifty_it_names()
    {
        var changes = Enumerable.Range(1, 60).Select(i => Repriced(i, 1m, 2m)).ToList();

        var rows = await HandleAsync(new PriceListUpdated(List, new PriceListSummary("x", false, null, 60), new PriceListSummary("x", false, null, 60), changes));

        var words = Enumerable.Range(1, 50).Select(i => $"SKU-{i} (PCS): changed from 1.00 to 2.00").Append("... and 10 more").ToArray();
        Assert.Equal([Expected("price-list.updated", Header("x", false, null, 60), Header("x", false, null, 60, words))], rows);
        var call = Assert.Single(_labels.Calls);
        Assert.Equal(Enumerable.Range(1, 50), call.Products);
    }

    [Theory]
    [InlineData(50, 0)]
    [InlineData(51, 1)]
    [InlineData(52, 2)]
    public async Task The_cap_is_fifty_words_and_the_rest_are_counted(int count, int more)
    {
        var rows = await HandleAsync(new PriceListDeleted(List, new PriceListSummary("x", false, null, count), Enumerable.Range(1, count).Select(i => Removed(i, 1m)).ToList()));

        var words = Enumerable.Range(1, 50).Select(i => $"SKU-{i} (PCS): removed (was 1.00)").ToList();
        if (more > 0) words.Add($"... and {more} more");
        Assert.Equal([Expected("price-list.deleted", Header("x", false, null, count, [.. words]), null)], rows);
    }

    // ----- customer set -----
    [Theory]
    [InlineData(null, 7, """{"CustomerId":null}""", """{"CustomerId":7}""")]
    [InlineData(7, 8, """{"CustomerId":7}""", """{"CustomerId":8}""")]
    [InlineData(8, null, """{"CustomerId":8}""", """{"CustomerId":null}""")]
    [InlineData(null, null, """{"CustomerId":null}""", """{"CustomerId":null}""")]
    public async Task CustomerSet_writes_the_internal_customer_before_and_after_and_asks_for_no_labels(int? before, int? after, string beforeJson, string afterJson)
    {
        var rows = await HandleAsync(new PriceListCustomerSet(List, before, after));

        Assert.Equal([Expected("price-list.customer-set", beforeJson, afterJson)], rows);
        Assert.Empty(_labels.Calls);
    }

    // ----- deleted -----
    [Fact]
    public async Task Deleted_writes_the_summary_with_every_price_as_removed_as_the_before_and_no_after()
    {
        var rows = await HandleAsync(new PriceListDeleted(List, new PriceListSummary("Doomed", false, 7, 2), [Removed(2, 22m, CartonId), Removed(1, 4.25m)]));

        Assert.Equal([Expected("price-list.deleted", Header("Doomed", false, 7, 2, "SKU-2 (CTN): removed (was 22.00)", "SKU-1 (PCS): removed (was 4.25)"), null)], rows);
        Assert.Single(_labels.Calls);
    }

    [Fact]
    public async Task Deleted_with_no_items_writes_a_before_with_no_price_words_and_asks_for_no_labels()
    {
        var rows = await HandleAsync(new PriceListDeleted(List, new PriceListSummary("Nothing", true, null, 0), []));

        Assert.Equal([Expected("price-list.deleted", Header("Nothing", true, null, 0), null)], rows);
        Assert.Empty(_labels.Calls);
    }

    // ----- actor and time -----
    [Fact]
    public async Task The_actor_is_the_current_user_and_the_time_is_the_clock_not_the_event()
    {
        var rows = await HandleAsync(new PriceListCustomerSet(List, null, 7));

        var row = Assert.Single(rows);
        Assert.Equal((Who, Now), (row.Actor, row.At));
    }
}
