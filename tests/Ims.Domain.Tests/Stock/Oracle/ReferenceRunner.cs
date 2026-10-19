namespace Ims.Domain.Tests.Stock.Oracle;

/// <summary>
/// The FROZEN reference implementation of the posting arithmetic, written straight from the rules (BR-02, BR-03, BR-05) and kept in the test project. It shares no code with
/// <c>PostingEngine</c>, <c>StockBalance</c> or <c>StockDocumentLine</c>, and every limit is written out as a literal, so a change to the real code (or to one of its constants)
/// that moves a number shows up as a difference between the two. Only the wording of the refusals comes from <see cref="Messages"/>. It is never updated to follow the engine:
/// when behaviour is meant to change, the change is announced in HANDOFFS.md and this file and the fixtures move together in a reviewed PR.
/// </summary>
internal sealed class ReferenceRunner
{
    private const int CostPlaces = 10;
    private const decimal MaxUnitCost = 1_000_000_000m;
    private const decimal MaxBaseQuantity = 99_999_999_999_999m;
    private const decimal MaxLineAmount = 999_999_999_999_999m;
    private const decimal MaxCostPerBaseUnit = 999_999_999_999m;
    private const int ReasonMinLength = 5;
    private const int PcsUom = 1;
    private const int CartonUom = 2;
    private const int WarehouseId = 1;

    private readonly Dictionary<int, Book> _books = [];
    private readonly Dictionary<string, Doc> _docs = [];
    private int _nextLineId = 1;

    public static IReadOnlyList<StepTrace> Run(IReadOnlyList<OracleStep> steps) =>
        OracleCulture.Invariant<IReadOnlyList<StepTrace>>(() =>
        {
            var runner = new ReferenceRunner();
            return steps.Select(runner.Execute).ToList();
        });

    private sealed class Book
    {
        public decimal Qty;
        public decimal Avg;
        public int? LastLine;

        public Book Copy() => (Book)MemberwiseClone();
    }

    private sealed class Line
    {
        public int Id;
        public int ProductId;
        public decimal Qty;
        public decimal BaseQty;
        public decimal UnitCost;
        public decimal EnteredCost;
        public decimal Amount;
        public decimal? AvgBefore;
        public decimal? AvgAfter;
        public int? PreviousLine;

        public Line Copy() => (Line)MemberwiseClone();
    }

    private sealed class Doc
    {
        public string No = "";
        public bool IsReceipt;
        public string Status = "Draft";
        public bool NeedsApproval;
        public bool NeedsOverride;
        public string? OverrideReason;
        public List<Line> Lines = [];

        public Doc Copy() => new() { No = No, IsReceipt = IsReceipt, Status = Status, NeedsApproval = NeedsApproval, NeedsOverride = NeedsOverride, OverrideReason = OverrideReason, Lines = [.. Lines.Select(l => l.Copy())] };
    }

    private sealed class Journal
    {
        public readonly List<TraceLedger> Ledger = [];
        public bool OverrideUsed;
    }

    private StepTrace Execute(OracleStep step)
    {
        if (step.Kind == StepKind.Reverse) return ReverseStep(step);

        Doc doc;
        try
        {
            doc = Build(step);
        }
        catch (Exception e)
        {
            return Trace(StepTrace.Describe(e), null, false, []);
        }
        _docs[doc.No] = doc;

        var work = doc.Copy();
        var books = Load(work);
        var run = new Journal();
        try
        {
            Post(work, books, run, step.AllowNegative, step.CanOverride);
        }
        catch (Exception e)
        {
            return Trace(StepTrace.Describe(e), doc, false, []);
        }
        return Commit(work, books, run);
    }

    private StepTrace ReverseStep(OracleStep step)
    {
        var doc = _docs[step.Doc];
        var work = doc.Copy();
        var books = Load(work);
        var run = new Journal();
        try
        {
            if (work.Status != "Posted") throw new DomainException(Messages.OnlyPostedCanReverse);
            if (string.IsNullOrWhiteSpace(step.Reason)) throw new DomainException(Messages.ReverseReasonRequired);
            if (step.Reason.Trim().Length < ReasonMinLength) throw new DomainException(Messages.ReasonTooShort);
            work.Status = "Reversed";

            var deficits = step.AllowNegative ? new SortedDictionary<int, decimal>() : Deficits(work, books);
            if (deficits.Count > 0)
                throw new ReversalBlockedException(work.No, deficits.Select(d => new ReversalShortage(Guid.Empty, OracleProducts.Sku(d.Key), "PCS", d.Value)).ToList());

            foreach (var line in work.Lines.OrderBy(l => l.ProductId).ThenByDescending(l => l.Id))
            {
                var book = books[line.ProductId];
                if (work.IsReceipt)
                {
                    Unreceive(book, line, step.AllowNegative);
                    run.Ledger.Add(new TraceLedger(line.ProductId, -line.BaseQty, book.Qty, line.UnitCost, "Reversal"));
                }
                else
                {
                    Receive(book, line.BaseQty, line.UnitCost, line.Id);
                    run.Ledger.Add(new TraceLedger(line.ProductId, +line.BaseQty, book.Qty, line.UnitCost, "Reversal"));
                }
            }
        }
        catch (Exception e)
        {
            return Trace(StepTrace.Describe(e), doc, false, []);
        }
        return Commit(work, books, run);
    }

    // ----- building a document -----

    private Doc Build(OracleStep step)
    {
        var isReceipt = step.Kind == StepKind.Receipt;
        var lines = step.Lines.Select(l => MakeLine(isReceipt, l)).ToList();

        var doc = new Doc { No = step.Doc, IsReceipt = isReceipt, Lines = lines };
        foreach (var line in lines) line.Id = _nextLineId++;

        doc.Status = "Submitted";
        switch (step.Approval)
        {
            case ApprovalState.Pending:
                doc.NeedsApproval = true;
                break;
            case ApprovalState.Approved:
                doc.NeedsApproval = true;
                doc.Status = "Approved";
                break;
            case ApprovalState.Override:
                doc.NeedsApproval = true;
                doc.NeedsOverride = true;
                doc.OverrideReason = step.Reason!.Trim();
                doc.Status = "Approved";
                break;
        }
        return doc;
    }

    private static Line MakeLine(bool isReceipt, OracleLine input)
    {
        if (input.Qty <= 0) throw new DomainException(Messages.QtyPositive);
        var typed = isReceipt ? input.Cost : null;
        if (typed > MaxUnitCost) throw new DomainException(Messages.CostTooLarge);
        if (typed < 0) throw new DomainException(Messages.CostNegative);
        if (typed is { } t && Math.Round(t, 2, MidpointRounding.AwayFromZero) != t) throw new DomainException(Messages.CostTooPrecise);

        var uom = input.UomId ?? PcsUom;
        var baseQty = input.Qty * FactorOf(input.ProductId, uom);
        if (baseQty > MaxBaseQuantity) throw new DomainException(Messages.QtyTooLarge);

        var entered = typed ?? 0m;
        var perBase = isReceipt && typed is not null && baseQty > 0 ? Math.Round(entered * input.Qty / baseQty, CostPlaces, MidpointRounding.AwayFromZero) : 0m;
        if (perBase > MaxCostPerBaseUnit) throw new DomainException(Messages.CostTooLargeForUnit);
        var amount = Math.Round(input.Qty * entered, 2, MidpointRounding.AwayFromZero);
        if (amount > MaxLineAmount) throw new DomainException(Messages.LineAmountTooLarge);

        return new Line { ProductId = input.ProductId, Qty = input.Qty, BaseQty = baseQty, UnitCost = perBase, EnteredCost = entered, Amount = amount };
    }

    private static decimal FactorOf(int productId, int uom)
    {
        if (uom == PcsUom) return 1m;
        if (uom == CartonUom && OracleProducts.CartonFactor(productId) is { } factor) return factor;
        throw new DomainException(Messages.ConversionMissing);
    }

    // ----- posting -----

    private static void Post(Doc doc, Dictionary<int, Book> books, Journal run, bool allowNegative, bool canOverride)
    {
        var canPost = doc.Status == "Approved" || (doc.Status == "Submitted" && !doc.NeedsApproval);
        if (!canPost) throw new DomainException(doc.Status == "Submitted" ? Messages.ApprovalRequiredToPost : Messages.CannotPostInStatus(Enum.Parse<DocumentStatus>(doc.Status)));

        foreach (var line in doc.Lines.OrderBy(l => l.ProductId).ThenBy(l => l.Id))
        {
            var book = books[line.ProductId];
            if (doc.IsReceipt)
            {
                var before = book.Avg;
                var previous = book.LastLine;
                Receive(book, line.BaseQty, line.UnitCost, line.Id);
                line.AvgBefore = before;
                line.AvgAfter = book.Avg;
                line.PreviousLine = previous;
                run.Ledger.Add(new TraceLedger(line.ProductId, +line.BaseQty, book.Qty, line.UnitCost, "Receipt"));
                continue;
            }

            if (book.Qty < line.BaseQty)
            {
                if (!allowNegative) throw new InsufficientStockException(line.ProductId, WarehouseId, book.Qty, line.BaseQty);
                if (!canOverride) throw new DomainException(Messages.NegativeNeedsSupervisor);
                if (string.IsNullOrWhiteSpace(doc.OverrideReason)) throw new DomainException(Messages.NegativeNeedsReason);
                run.OverrideUsed = true;
            }
            var cost = book.Avg;
            book.Qty -= line.BaseQty;
            line.UnitCost = cost;
            line.Amount = Math.Min(Math.Round(line.BaseQty * cost, 2, MidpointRounding.AwayFromZero), MaxLineAmount);
            line.EnteredCost = line.Qty > 0 ? Math.Min(Math.Round(cost * line.BaseQty / line.Qty, 2, MidpointRounding.AwayFromZero), MaxLineAmount) : 0m;
            run.Ledger.Add(new TraceLedger(line.ProductId, -line.BaseQty, book.Qty, cost, "Issue"));
        }
        doc.Status = "Posted";
    }

    private static void Receive(Book book, decimal qty, decimal cost, int lineId)
    {
        var newQty = book.Qty + qty;
        if (book.Qty <= 0 || newQty <= 0) book.Avg = cost;
        else if (cost != book.Avg) book.Avg = Math.Round((book.Qty * book.Avg + qty * cost) / newQty, CostPlaces, MidpointRounding.AwayFromZero);
        book.Qty = newQty;
        book.LastLine = lineId > 0 ? lineId : null;
    }

    // ----- reversing -----

    private static SortedDictionary<int, decimal> Deficits(Doc doc, Dictionary<int, Book> books)
    {
        var result = new SortedDictionary<int, decimal>();
        foreach (var product in doc.Lines.GroupBy(l => l.ProductId).OrderBy(g => g.Key))
        {
            var change = product.Sum(l => doc.IsReceipt ? -l.BaseQty : +l.BaseQty);
            var after = books[product.Key].Qty + change;
            if (change < 0 && after < 0) result[product.Key] = after;
        }
        return result;
    }

    private static void Unreceive(Book book, Line line, bool allowNegative)
    {
        if (!allowNegative && book.Qty < line.BaseQty) throw new InsufficientStockException(line.ProductId, WarehouseId, book.Qty, line.BaseQty);

        var remaining = book.Qty - line.BaseQty;
        if (line.Id > 0 && book.LastLine == line.Id && line.AvgBefore is { } before && line.AvgAfter is not null)
        {
            book.Avg = before == 0m && remaining < 0m ? book.Avg : before;
            book.LastLine = line.PreviousLine;
        }
        else
        {
            book.Avg = AverageAfterTakingOut(book, line, remaining);
            book.LastLine = null;
        }
        book.Qty = remaining;
    }

    private static decimal AverageAfterTakingOut(Book book, Line line, decimal remaining)
    {
        if (remaining <= 0 || book.Qty <= 0) return book.Avg;
        var valueLeft = book.Qty * book.Avg - line.BaseQty * line.UnitCost;
        return valueLeft > 0 ? Math.Min(Math.Round(valueLeft / remaining, CostPlaces, MidpointRounding.AwayFromZero), MaxCostPerBaseUnit) : book.Avg;
    }

    // ----- the book -----

    private Dictionary<int, Book> Load(Doc doc) =>
        doc.Lines.Select(l => l.ProductId).Distinct().ToDictionary(id => id, id => _books.TryGetValue(id, out var b) ? b.Copy() : new Book());

    private StepTrace Commit(Doc work, Dictionary<int, Book> books, Journal run)
    {
        _docs[work.No] = work;
        foreach (var (id, book) in books) _books[id] = book;
        return Trace(StepTrace.Ok, work, run.OverrideUsed, run.Ledger);
    }

    private StepTrace Trace(string outcome, Doc? doc, bool overrideUsed, IReadOnlyList<TraceLedger> ledger) => new(
        outcome, doc?.Status ?? StepTrace.NoDocument, overrideUsed, [.. ledger],
        [.. (doc?.Lines ?? []).OrderBy(l => l.Id).Select(l => new TraceLine(l.Id, l.ProductId, l.BaseQty, l.UnitCost, l.EnteredCost, l.Amount, l.AvgBefore, l.AvgAfter, l.PreviousLine))],
        [.. _books.OrderBy(b => b.Key).Select(b => new TraceBalance(b.Key, b.Value.Qty, b.Value.Avg, b.Value.LastLine))]);
}
