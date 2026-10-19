using static Ims.Domain.Tests.TestData;

namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// Runs a sequence on the real <see cref="PostingEngine"/> the way the post and reverse handlers do: build and submit (and approve) the document, load the balances of its
/// products (a missing one opens at zero), run the engine, and keep the result only when nothing threw. Everything a step leaves is written down as a <see cref="StepTrace"/>.
/// </summary>
internal sealed class EngineRunner
{
    private const int WarehouseId = 1;
    private const string ShortageUomCode = "PCS";

    private readonly PostingEngine _engine = NewEngine();
    private readonly Dictionary<int, StockBalance> _balances = [];
    private readonly Dictionary<string, StockDocument> _documents = [];
    private int _nextDocumentId = 1;
    private int _nextLineId = 1;

    public static IReadOnlyList<StepTrace> Run(IReadOnlyList<OracleStep> steps) =>
        OracleCulture.Invariant<IReadOnlyList<StepTrace>>(() =>
        {
            var runner = new EngineRunner();
            return steps.Select(runner.Execute).ToList();
        });

    private StepTrace Execute(OracleStep step) => step.Kind == StepKind.Reverse ? Reverse(step) : Post(step);

    private StepTrace Post(OracleStep step)
    {
        StockDocument document;
        try
        {
            document = Build(step);
        }
        catch (Exception e)
        {
            return Trace(StepTrace.Describe(e), null, false, []);
        }
        _documents[step.Doc] = document;

        var work = OracleClone.Copy(document);
        var balances = Load(work);
        var context = new PostingContext(work, Settings(step.AllowNegative), Approver, step.CanOverride, Now);
        try
        {
            _engine.Post(work, balances, context);
        }
        catch (Exception e)
        {
            return Trace(StepTrace.Describe(e), document, false, []);
        }
        return Commit(work, balances, context);
    }

    private StepTrace Reverse(OracleStep step)
    {
        var document = _documents[step.Doc];
        var work = OracleClone.Copy(document);
        var balances = Load(work);
        var settings = Settings(step.AllowNegative);
        var context = new PostingContext(work, settings, Reverser, canOverrideNegative: false, Now);
        try
        {
            work.MarkReversed(step.Reason!, Now, Reverser);
            var deficits = _engine.FindReversalDeficits(work, balances, settings);
            if (deficits.Count > 0)
                throw new ReversalBlockedException(work.No, deficits.Select(d => new ReversalShortage(Guid.Empty, OracleProducts.Sku(d.Key), ShortageUomCode, d.Value)).ToList());
            _engine.Reverse(work, balances, context);
        }
        catch (Exception e)
        {
            return Trace(StepTrace.Describe(e), document, false, []);
        }
        return Commit(work, balances, context);
    }

    private StockDocument Build(OracleStep step)
    {
        var type = step.Kind == StepKind.Receipt ? DocumentType.Receipt : DocumentType.Issue;
        var lines = step.Lines.Select(l => NewLine(OracleProducts.Create(l.ProductId), type, l.Qty, l.Cost, l.UomId)).ToList();

        var document = type == DocumentType.Receipt
            ? StockDocument.Create(type, NewWarehouse(), NewSupplier(), null, null, Creator, Now)
            : StockDocument.Create(type, NewWarehouse(), NewCustomer(), IssuePurpose.Sale, null, Creator, Now);
        document.WithId(_nextDocumentId++);
        document.AssignNumber(step.Doc);
        foreach (var line in lines) line.WithId(_nextLineId++);
        document.AddLines(lines);

        switch (step.Approval)
        {
            case ApprovalState.NotNeeded:
                document.Submit(Submitter, requiresApproval: false, Now);
                break;
            case ApprovalState.Pending:
                document.Submit(Submitter, requiresApproval: true, Now);
                break;
            case ApprovalState.Approved:
                document.Submit(Submitter, requiresApproval: true, Now);
                document.Approve(Approver, allowSelfApproval: false, Now);
                break;
            case ApprovalState.Override:
                document.Submit(Submitter, requiresApproval: true, Now, requiresOverride: true);
                document.Approve(Approver, allowSelfApproval: false, Now, step.Reason);
                break;
        }
        return document;
    }

    /// <summary>The balances of the document's products, copied so that a failed run changes nothing; a product never stocked opens at zero.</summary>
    private Dictionary<int, StockBalance> Load(StockDocument document) =>
        document.Lines.Select(l => l.ProductId).Distinct().ToDictionary(
            id => id, id => _balances.TryGetValue(id, out var existing) ? OracleClone.Copy(existing) : StockBalance.OpenFor(id, WarehouseId));

    private StepTrace Commit(StockDocument work, Dictionary<int, StockBalance> balances, PostingContext context)
    {
        _documents[work.No] = work;
        foreach (var (id, balance) in balances) _balances[id] = balance;
        return Trace(StepTrace.Ok, work, context.OverrideUsed, context.Entries);
    }

    private StepTrace Trace(string outcome, StockDocument? document, bool overrideUsed, IReadOnlyList<StockLedgerEntry> entries) => new(
        outcome, document?.Status.ToString() ?? StepTrace.NoDocument, overrideUsed,
        [.. entries.Select(e => new TraceLedger(e.ProductId, e.QtyDelta, e.BalanceAfter, e.UnitCost, e.Reason.ToString()))],
        [.. (document?.Lines ?? []).OrderBy(l => l.Id).Select(l => new TraceLine(
            l.Id, l.ProductId, l.BaseQty, l.UnitCost, l.EnteredUnitCost, l.Amount, l.AvgCostBefore, l.AvgCostAfter, l.PreviousLineId))],
        [.. _balances.Values.OrderBy(b => b.ProductId).Select(b => new TraceBalance(b.ProductId, b.QtyOnHand, b.AvgCost, b.LastReceiptLineId))]);
}
