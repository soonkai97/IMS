using System.Globalization;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>
/// Writes the audit row of every change to a price list (DDD-13, docs/architecture/ddd-roadmap.md 2.4, 2.5). It runs INSIDE the transaction of the use case, before the unit of work saves, so the row is written by the
/// same save as the change and commits or rolls back with it (AUTH-03). It reads ONLY the facts of the event (never an entity, never a <c>REC_*</c> column) and stages the row through <see cref="IAuditTrail"/> as the
/// handlers did before: the same action, entity type, entity id (the public id of the list), the same Before and After keys in the same order, the same <see cref="AuditFields"/> tag on the price words (so the
/// scrubber still withholds them from a caller without <c>pricelist.manage</c>), and the actor and the time are read from the current user and the clock at this moment, as <c>AuditTrail.Record</c> does.
/// <para>
/// The prices of a save are written in words, "SKU (UNIT): changed from 3.00 to 3.50": the event carries the product and the unit by integer key and the subscriber names them with ONE lookup per event
/// (<see cref="IPriceListLabelReader"/>), asked only for the items the row names (at most <see cref="MaxChangesInWords"/>; the rest are counted). A row with no price changes asks for nothing.
/// </para>
/// </summary>
public sealed class PriceListAuditSubscriber(IAuditTrail audit, IPriceListLabelReader labels) : IAppService,
    IDomainEventHandler<PriceListCreated>, IDomainEventHandler<PriceListUpdated>, IDomainEventHandler<PriceListCustomerSet>, IDomainEventHandler<PriceListDeleted>
{
    /// <summary>At most this many changed prices are written in words; the rest are counted. A save may touch thousands of items and the audit row stays small.</summary>
    internal const int MaxChangesInWords = 50;

    public async Task HandleAsync(PriceListCreated domainEvent, CancellationToken ct)
    {
        var words = await WordsAsync(domainEvent.Changes, ct);
        audit.Record(AuditActions.PriceListCreated, AuditEntities.PriceList, domainEvent.ListId.ToString(), after: Summary(domainEvent.Summary, words));
    }

    public async Task HandleAsync(PriceListUpdated domainEvent, CancellationToken ct)
    {
        var words = await WordsAsync(domainEvent.Changes, ct);
        audit.Record(AuditActions.PriceListUpdated, AuditEntities.PriceList, domainEvent.ListId.ToString(), Summary(domainEvent.Before, []), Summary(domainEvent.After, words));
    }

    public Task HandleAsync(PriceListCustomerSet domainEvent, CancellationToken ct)
    {
        audit.Record(AuditActions.PriceListCustomerSet, AuditEntities.PriceList, domainEvent.ListId.ToString(), new { CustomerId = domainEvent.CustomerBefore }, new { CustomerId = domainEvent.CustomerAfter });
        return Task.CompletedTask;
    }

    /// <summary>
    /// A deleted list is written as its summary with its prices in words (the first 50, then a count) as the BEFORE and no after. The prices are not lost with the audit's cap: a deleted list and its items are
    /// soft-deleted, so every price stays in the database, and a restore is a data fix, not a reconstruction from the audit log.
    /// </summary>
    public async Task HandleAsync(PriceListDeleted domainEvent, CancellationToken ct)
    {
        var words = await WordsAsync(domainEvent.Changes, ct);
        audit.Record(AuditActions.PriceListDeleted, AuditEntities.PriceList, domainEvent.ListId.ToString(), Summary(domainEvent.Summary, words));
    }

    /// <summary>The header of a list, keys in the order the handlers always wrote them, and the price words when there are any.</summary>
    private static Dictionary<string, object?> Summary(PriceListSummary summary, IReadOnlyCollection<string> words)
    {
        var payload = new Dictionary<string, object?>
        {
            [nameof(summary.Name)] = summary.Name,
            [nameof(summary.IsDefault)] = summary.IsDefault,
            [nameof(summary.CustomerId)] = summary.CustomerId,
            ["Items"] = summary.ItemCount
        };
        if (words.Count > 0) payload[AuditFields.ListPriceChanges] = words;
        return payload;
    }

    /// <summary>The first 50 changes in words and a count of the rest; one lookup names the products and units of those 50 and no more. No change, no lookup.</summary>
    private async Task<List<string>> WordsAsync(IReadOnlyList<PriceChange> changes, CancellationToken ct)
    {
        if (changes.Count == 0) return [];

        var named = changes.Take(MaxChangesInWords).ToList();
        var found = await labels.ReadAsync(named.Select(c => c.ProductId).Distinct().ToList(), named.Select(c => c.UomId).Distinct().ToList(), ct);
        var words = named.Select(c => Line(c, found)).ToList();
        if (changes.Count > MaxChangesInWords) words.Add($"... and {changes.Count - MaxChangesInWords} more");
        return words;
    }

    private static string Line(PriceChange change, PriceListLabels found)
    {
        var name = $"{found.Skus.GetValueOrDefault(change.ProductId, "")} ({found.UomCodes.GetValueOrDefault(change.UomId, "")})";
        return change.Kind switch
        {
            PriceChangeKind.Added => $"{name}: added at {Places(change.Now)}",
            PriceChangeKind.Removed => $"{name}: removed (was {Places(change.Was)})",
            _ => $"{name}: changed from {Places(change.Was)} to {Places(change.Now)}"
        };
    }

    /// <summary>A price with two places, whatever scale it was entered in.</summary>
    private static string Places(Money? price) => (price ?? Money.Zero).Value.ToString("0.00", CultureInfo.InvariantCulture);
}
