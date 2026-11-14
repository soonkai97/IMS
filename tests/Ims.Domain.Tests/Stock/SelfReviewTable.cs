using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// DDD-09: may the person who submitted a document approve or reject it? Written from the rule (BR-07): not, unless the customer allows self-approval, and never for a return, whatever the setting says.
/// One table for the approve and reject handlers (before the change and after it) and for the domain (<c>StockDocument.Approve</c> and <c>Reject</c> with the setting as it is stored).
/// </summary>
internal static class SelfReviewTable
{
    internal enum Kind { Ordinary, CustomerReturn, SupplierReturn }

    internal sealed record Case(bool SettingOn, Kind Kind, bool SameUser)
    {
        public int Reviewer => SameUser ? Submitter : Approver;
        public bool Allowed => !SameUser || (SettingOn && Kind == Kind.Ordinary);
        public override string ToString() => $"setting {(SettingOn ? "on" : "off")}, {Kind}, {(SameUser ? "the submitter" : "another reviewer")}";
    }

    internal static IEnumerable<Case> All() =>
        from setting in new[] { false, true }
        from kind in Enum.GetValues<Kind>()
        from submitter in new[] { false, true }
        select new Case(setting, kind, submitter);

    /// <summary>A document submitted by <see cref="TestData.Submitter"/> and held for approval.</summary>
    internal static StockDocument Submitted(Kind kind)
    {
        var doc = kind switch
        {
            Kind.Ordinary => NewReceipt(ReturnableTable.Id, (NewProduct(), 5m, 2m)),
            Kind.CustomerReturn => StockDocument.CreateReturn(ReturnableTable.PostedSale(), Creator, Now).WithId(ReturnableTable.Id),
            _ => StockDocument.CreateReturn(ReturnableTable.Posted(NewReceipt(ReturnableTable.Original, (NewProduct(), 5m, 2m))), Creator, Now).WithId(ReturnableTable.Id),
        };
        if (kind != Kind.Ordinary) doc.AddLines([NewLine(NewProduct(), doc.Type, 1m)]);
        doc.Submit(Submitter, requiresApproval: true, Now);
        return doc;
    }
}
