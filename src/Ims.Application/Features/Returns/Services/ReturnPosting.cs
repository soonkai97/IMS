using Ims.Application.Features.Documents;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Returns;

/// <summary>
/// The original-document part of posting a return (PL3.16), kept out of <c>PostDocumentHandler</c> so that a document that is not a return runs exactly the statements it ran before. Lock order: the original
/// <c>ret</c> lock FIRST (nothing has locked stock yet; the balance loader does it after this returns), then the original and the posted returned quantities are read fresh (the lock is what makes them the truth),
/// then the checks. This is the AUTHORITATIVE over-return check: the ones at save and submit are advice, they hold nothing. A customer return takes the goods back at the cost they left at (fixed on its lines); a
/// supplier return is an ordinary issue at the average cost, so the engine and the strategies are untouched.
/// </summary>
public sealed class ReturnPosting(IReadRepository<StockDocument> documents, IReturnedQuantityReader returned, IModuleSwitches modules, ITransactionLocker locker, ICallerPermissions caller) : IDocumentLinkPosting, IAppService
{
    public DocumentLinkKind Kind => DocumentLinkKind.Return;

    public async Task<LinkedPostingState?> BeforePostAsync(StockDocument document, CancellationToken ct)
    {
        if (document.ReturnOfDocumentId is not { } key) return null;

        await locker.LockReturnOriginAsync(key, ct);
        // The document was read before the lock. If it changed while this posting waited (the same return posted, reversed or cancelled by someone else) the whole posting starts again on the new state.
        var stamp = await documents.FirstOrDefaultAsync(new DocumentStampSpec(document.Id), ct);
        if (stamp is null || !stamp.RowVersion.AsSpan().SequenceEqual(document.RowVersion)) throw new ConcurrencyException(Messages.ConcurrencyConflict);
        await ReturnGuard.EnsureAsync(DocumentActions.Post, caller, modules, ct);
        var original = await documents.FirstOrDefaultAsync(new DocumentForReturnSpec(key), ct) ?? throw new NotFoundException(Messages.ReturnOriginalMissing);
        if (original.Status != DocumentStatus.Posted) throw new ConflictException(Messages.ReturnOriginalNotPosted(original.No, original.Status));

        var positions = DocumentLinePositions.Of(original.Lines);
        var done = await returned.PostedByLineAsync(key, ct);
        var over = ReturnQuantities.OverReturns(positions, done,
            document.Lines.Where(l => l.ReturnOfLineNo is not null).Select(l => new ReturnQuantity(l.ReturnOfLineNo!.Value, l.Qty)));
        if (over.Count == 0)
        {
            var exhausted = ReturnBaseAlignment.Apply(positions, done, document.Lines); // under the lock: the returns before this one are final, so the last pieces of a line take the base quantity that is left
            if (exhausted.Count > 0) throw new ReturnLineExhaustedException(string.Join(" ", exhausted.Select(e => e.LineNo).Distinct().Select(n => Messages.ReturnLineBaseExhausted(n, positions.First(p => p.LineNo == n).Line.Product.Sku))));
            return null;
        }
        var lines = over.Select(o =>
        {
            var line = positions.First(p => p.LineNo == o.LineNo).Line;
            return (Over: o, Sku: line.Product.Sku, Uom: line.Uom.Code);
        }).ToList();
        throw new OverReturnException(string.Join(" ", lines.Select(x => Messages.ReturnLineOverReturn(x.Over, x.Sku, x.Uom))),
            lines.Select(x => new OverReturnLine(x.Over.LineNo, x.Sku, x.Uom, x.Over.Original, x.Over.Returned, x.Over.Remaining, x.Over.Requested)).ToList());
    }

    public void AfterPost(LinkedPostingState? state, StockDocument document)
    {
        // Nothing to close: the returned quantity is computed from the Posted returns, never stored.
    }
}
