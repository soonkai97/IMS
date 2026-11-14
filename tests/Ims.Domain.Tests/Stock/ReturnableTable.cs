using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// DDD-09: which documents can be returned, written from the rule and not from the code: a Posted receipt, or a Posted issue for a sale, that is not itself a return. Each row also says the steps
/// the reverse handler takes for that document before it reverses it (its locks, in order, and whether it asks if a Posted return of it exists): only a document that can be returned takes its own
/// <c>ret</c> lock and asks; a return takes the lock of its original; the order's lock comes first. One table for the handler (before the change, and after it) and for the domain member.
/// </summary>
internal static class ReturnableTable
{
    internal const int Id = 10;

    internal sealed record Row(string Name, Func<StockDocument> Build, bool Returnable, string[] Steps)
    {
        public override string ToString() => Name;
    }

    internal static IEnumerable<Row> All() =>
    [
        new("posted receipt", () => Posted(NewReceipt(Id, (NewProduct(), 5m, 2m))), true, [$"lock ret {Id}", "query"]),
        new("posted receipt from a purchase order", () => Posted(NewReceipt(Id, (NewProduct(), 5m, 2m)).WithPrivate("PurchaseOrderId", 9)), true, [$"lock ret {Id}", "query"]),
        new("posted issue for a sale", () => Posted(NewIssue(Id, (NewProduct(), 5m))), true, [$"lock ret {Id}", "query"]),
        new("posted delivery from a sales order", () => Posted(NewIssue(Id, (NewProduct(), 5m)).WithPrivate("SalesOrderId", 7)), true, ["lock so 7", $"lock ret {Id}", "query"]),
        new("posted issue for use", () => Posted(Issue(IssuePurpose.Use)), false, []),
        new("posted issue for scrap", () => Posted(Issue(IssuePurpose.Scrap)), false, []),
        new("posted customer return", () => PostedReturnOf(PostedSale()), false, [$"lock ret {Original}"]),
        new("posted supplier return", () => PostedReturnOf(Posted(NewReceipt(Original, (NewProduct(), 5m, 2m)))), false, [$"lock ret {Original}"]),
        new("reversed receipt", () => Reversed(NewReceipt(Id, (NewProduct(), 5m, 2m))), false, []),
        new("reversed issue for a sale", () => Reversed(NewIssue(Id, (NewProduct(), 5m))), false, []),
        new("cancelled receipt", () => Cancelled(NewReceipt(Id, (NewProduct(), 5m, 2m))), false, []),
        new("cancelled issue for a sale", () => Cancelled(NewIssue(Id, (NewProduct(), 5m))), false, []),
        new("draft receipt", () => NewReceipt(Id, (NewProduct(), 5m, 2m)), false, []),
        new("submitted receipt", () => NewReceipt(Id, (NewProduct(), 5m, 2m)).Submitted(), false, []),
        new("approved receipt", () => NewApprovedReceipt().WithId(Id), false, []),
    ];

    /// <summary>The key of the document a return names.</summary>
    internal const int Original = 5;

    internal static StockDocument Posted(StockDocument doc)
    {
        if (doc.Status == DocumentStatus.Draft) doc.Submitted();
        doc.MarkPosted(Approver, Now);
        return doc;
    }

    internal static StockDocument PostedSale() => Posted(NewIssue(Original, (NewProduct(), 5m)));

    /// <summary>The Posted return of <paramref name="original"/>: a receipt when the original was a sale, an issue when it was a receipt.</summary>
    internal static StockDocument PostedReturnOf(StockDocument original, int id = Id)
    {
        var draft = StockDocument.CreateReturn(original, Creator, Now).WithId(id);
        draft.AddLines([NewLine(NewProduct(), draft.Type, 1m)]);
        return Posted(draft);
    }

    private static StockDocument Issue(IssuePurpose purpose)
    {
        var doc = StockDocument.Create(DocumentType.Issue, NewWarehouse(), NewCustomer(), purpose, null, Creator, Now).WithId(Id);
        doc.AddLines([NewLine(NewProduct(), DocumentType.Issue, 5m)]);
        return doc;
    }

    private static StockDocument Reversed(StockDocument doc)
    {
        Posted(doc).MarkReversed("entered twice", Now, Reverser);
        return doc;
    }

    private static StockDocument Cancelled(StockDocument doc)
    {
        doc.Cancel(null);
        return doc;
    }
}
