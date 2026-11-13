using Ims.Application.Features.Documents;
using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Returns;

/// <summary>
/// Creates the Draft return of a Posted document: a customer return (a Receipt) of a sale issue, or a supplier return (an Issue) of a receipt, prefilled with one line for every original line that still has something
/// to return, in the original unit, with the quantity that can still be returned. The door is <c>returns.create</c> and the module; the original must be readable (inside the caller warehouses). Lock order: the shared
/// <c>modules</c> lock first (the module is held on, as a purchase order create does), then the original <c>ret</c> lock, then the document-sequence lock, and the original is read after its lock, so the quantities
/// and the status seen are the truth for this transaction.
/// </summary>
public sealed class CreateReturnHandler(IReadRepository<StockDocument> documents, IRepository<StockDocument> writes, IReturnedQuantityReader returned, IDocumentAccess access, IDocumentNumbering numbering,
    IDocumentReader reader, ICurrentUser current, IClock clock, ITransactionLocker locker, IModuleSwitches switches, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CreateReturnCommand, DocumentDto>
{
    public async Task<DocumentDto> HandleAsync(CreateReturnCommand cmd, CancellationToken ct)
    {
        // The whole body re-runs if two requests race for the same document number or the original lock.
        var publicId = await uow.ExecuteAsync(async () =>
        {
            await locker.HoldOnAsync(switches, ModuleCodes.Returns, Messages.RouteNotFound, ct);
            // The caller scope is judged on the key BEFORE the original lock is taken, so a caller who may not see the document cannot make others wait on its lock.
            var found = await documents.FirstOrDefaultAsync(new ByPublicIdSpec<StockDocument>(cmd.Data.OriginalDocumentId, tracked: false), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
            access.EnsureDocumentInScope(found.WarehouseId);
            var key = found.Id;
            await locker.LockReturnOriginAsync(key, ct);
            var original = await documents.FirstOrDefaultAsync(new DocumentForReturnSpec(key), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);

            // What can still be returned of each line, in the original unit: the quantity of the line less what Posted returns took back. A line with nothing left is not copied.
            var done = await returned.PostedByLineAsync(key, ct);
            var open = DocumentLinePositions.Of(original.Lines).Where(p => ReturnQuantities.Remaining(p.Line.Qty, done.GetValueOrDefault(p.LineNo).Qty) > 0 && p.Line.BaseQty - done.GetValueOrDefault(p.LineNo).BaseQty > 0m).ToList(); // a line whose base quantity is all back (tiny factor) cannot be returned: it is left out
            StockDocument draft;
            try
            {
                draft = StockDocument.CreateReturn(original, current.UserId, clock.UtcNow); // refuses a document that is not Posted, is a return, or is not a sale or a receipt
            }
            catch (DomainException e)
            {
                throw new ConflictException(e.Message); // the state of the document, not a bad field: 409 with the sentence
            }
            if (open.Count == 0) throw new ConflictException(Messages.ReturnNothingToReturn(original.No));

            draft.AssignNumber(await numbering.NextAsync(draft.Type, original.Warehouse, ct));
            draft.AddLines(open.Select(p => StockDocumentLine.CreateReturn(p.Line.Product, draft.Type, ReturnQuantities.Remaining(p.Line.Qty, done.GetValueOrDefault(p.LineNo).Qty), p.Line.UomId,
                remark: null, p.LineNo, p.Line.UnitCost)));

            writes.Add(draft);
            audit.Record(AuditActions.DocumentCreated, AuditEntities.StockDocument, draft.No,
                after: new { draft.Type, draft.WarehouseId, Lines = draft.Lines.Count, ReturnOf = original.No });
            await uow.SaveChangesAsync(ct);
            return draft.PublicId;
        }, ct);
        return await reader.GetAsync(publicId, ct);
    }
}
