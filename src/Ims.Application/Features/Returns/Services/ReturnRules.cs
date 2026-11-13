using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Returns;

public class ReturnRules(IReadRepository<StockDocument> documents, IReturnedQuantityReader returned, IModuleSwitches modules, ICallerPermissions caller) : IReturnRules, IAppService
{
    public async Task<StockDocument> OriginalForSaveAsync(StockDocument draft, Partner? partner, IssuePurpose? purpose, CancellationToken ct)
    {
        draft.EnsureEditable();
        // Who may change a return draft, and while the module is on, is one rule with its allowedActions (LinkedReceiptPolicy): the original is not looked at before it passes.
        await ReturnGuard.EnsureAsync(DocumentActions.Edit, caller, modules, ct);
        var original = await documents.FirstOrDefaultAsync(new DocumentForReturnSpec(draft.ReturnOfDocumentId!.Value), ct) ?? throw new NotFoundException(Messages.ReturnOriginalMissing);
        if (original.Status != DocumentStatus.Posted) throw new ConflictException(Messages.ReturnOriginalNotPosted(original.No, original.Status));
        if (partner?.Id != draft.PartnerId)
            throw new ValidationException(Messages.ReturnHeaderLocked, new Dictionary<string, string[]> { ["partnerId"] = [Messages.ReturnHeaderLocked] });
        if (purpose != draft.Purpose)
            throw new ValidationException(Messages.ReturnHeaderLocked, new Dictionary<string, string[]> { ["purpose"] = [Messages.ReturnHeaderLocked] });
        return original;
    }

    public async Task EnsureCanSubmitAsync(StockDocument draft, CancellationToken ct)
    {
        if (draft.ReturnOfDocumentId is not { } key) return;
        await ReturnGuard.EnsureAsync(DocumentActions.Submit, caller, modules, ct);
        var original = await documents.FirstOrDefaultAsync(new DocumentForReturnSpec(key), ct) ?? throw new NotFoundException(Messages.ReturnOriginalMissing);
        if (original.Status != DocumentStatus.Posted) throw new ConflictException(Messages.ReturnOriginalNotPosted(original.No, original.Status));
        await EnsureWithinRemainingAsync(original, draft.Lines.ToList(), ct);
    }

    public async Task EnsureWithinRemainingAsync(StockDocument original, IReadOnlyList<StockDocumentLine> lines, CancellationToken ct)
    {
        var positions = DocumentLinePositions.Of(original.Lines);
        var done = await returned.PostedByLineAsync(original.Id, ct);
        var over = ReturnQuantities.OverReturns(positions, done,
            lines.Where(l => l.ReturnOfLineNo is not null).Select(l => new ReturnQuantity(l.ReturnOfLineNo!.Value, l.Qty)));
        if (over.Count == 0)
        {
            ReturnBaseAlignment.Apply(positions, done, lines); // the draft shows the base quantity it will post with
            return;
        }
        var errors = new Dictionary<string, string[]>();
        foreach (var o in over)
        {
            var line = positions.First(p => p.LineNo == o.LineNo).Line;
            var firstIndex = lines.ToList().FindIndex(l => l.ReturnOfLineNo == o.LineNo);
            errors[ErrorKeys.Normalize($"Data.Lines[{firstIndex}].Qty")] = [Messages.ReturnLineOverReturn(o, line.Product.Sku, line.Uom.Code)];
        }
        throw new ValidationException(errors.First().Value[0], errors);
    }
}
