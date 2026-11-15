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

    /// <param name="HasRow">Whether a balance row is stored for the product: no row means zero on hand, and so does a stored 0 row; a stored row can also be negative (negative stock was allowed once).</param>
    internal sealed record Case(DocumentType Type, bool NegativeAllowed, decimal OnHand, bool RulesRequireApproval, bool HasRow = true)
    {
        public bool IsShort => OnHand < Needed;

        /// <summary>The receipt is not looked at; an issue short of stock is refused unless negative stock is allowed, and then flagged.</summary>
        public Outcome Expected => Type == DocumentType.Issue && IsShort ? (NegativeAllowed ? Outcome.Override : Outcome.Refused) : Outcome.NoOverride;

        public bool GoesToAnApprover => RulesRequireApproval || Expected == Outcome.Override;
        public StockDocument Document() => Type == DocumentType.Issue ? NewIssue(ReturnableTable.Id, (NewProduct(), Needed)) : NewReceipt(ReturnableTable.Id, (NewProduct(), Needed, 2m));
        public IReadOnlyDictionary<int, decimal> OnHandByProduct() => HasRow ? new Dictionary<int, decimal> { [1] = OnHand } : new Dictionary<int, decimal>();

        /// <summary>The balance rows the repository holds for the product: none, or one with the stored quantity (a negative one is made the way posting makes it, by issuing past zero).</summary>
        public IReadOnlyList<StockBalance> Balances()
        {
            if (!HasRow) return [];
            var balance = NewBalance(1, Math.Max(OnHand, 0m), 2m);
            if (OnHand < 0) balance.Issue(-OnHand, allowNegative: true);
            return [balance];
        }

        public override string ToString() =>
            $"{Type}, negative stock {(NegativeAllowed ? "allowed" : "not allowed")}, {(HasRow ? OnHand.ToString() : "no row")} on hand of {Needed}, rules {(RulesRequireApproval ? "ask" : "do not ask")} for approval";
    }

    internal static IEnumerable<Case> All() =>
        from type in new[] { DocumentType.Issue, DocumentType.Receipt }
        from negative in new[] { false, true }
        from stock in new (decimal OnHand, bool HasRow)[] { (0m, false), (0m, true), (-3m, true), (4.9999m, true), (Needed, true), (50m, true) }
        from rules in new[] { false, true }
        select new Case(type, negative, stock.OnHand, rules, stock.HasRow);
}
