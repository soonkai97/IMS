using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock;

/// <summary>
/// DDD-09: what a submit does about short stock (OUT-02, BR-03), written from the rule: only an issue takes stock out, so only an issue is checked. Enough stock: nothing to decide. Short stock: refused
/// while negative stock is not allowed; sent on with an override flag when it is. A document with an override always goes to an approver, whatever the value rules say.
/// One table for the submit handler (before the change and after it) and for the domain policy.
/// </summary>
internal static class ShortStockTable
{
    internal const decimal Needed = 5m;

    internal enum Outcome { Refused, NoOverride, Override }

    internal sealed record Case(DocumentType Type, bool NegativeAllowed, decimal OnHand, bool RulesRequireApproval)
    {
        public bool IsShort => OnHand < Needed;

        /// <summary>The receipt is not looked at; an issue short of stock is refused unless negative stock is allowed, and then flagged.</summary>
        public Outcome Expected => Type == DocumentType.Issue && IsShort ? (NegativeAllowed ? Outcome.Override : Outcome.Refused) : Outcome.NoOverride;

        public bool GoesToAnApprover => RulesRequireApproval || Expected == Outcome.Override;
        public StockDocument Document() => Type == DocumentType.Issue ? NewIssue(ReturnableTable.Id, (NewProduct(), Needed)) : NewReceipt(ReturnableTable.Id, (NewProduct(), Needed, 2m));
        public IReadOnlyDictionary<int, decimal> OnHandByProduct() => OnHand > 0 ? new Dictionary<int, decimal> { [1] = OnHand } : new Dictionary<int, decimal>();
        public override string ToString() => $"{Type}, negative stock {(NegativeAllowed ? "allowed" : "not allowed")}, {OnHand} on hand of {Needed}, rules {(RulesRequireApproval ? "ask" : "do not ask")} for approval";
    }

    internal static IEnumerable<Case> All() =>
        from type in new[] { DocumentType.Issue, DocumentType.Receipt }
        from negative in new[] { false, true }
        from onHand in new[] { 0m, 4.9999m, Needed, 50m }
        from rules in new[] { false, true }
        select new Case(type, negative, onHand, rules);
}
