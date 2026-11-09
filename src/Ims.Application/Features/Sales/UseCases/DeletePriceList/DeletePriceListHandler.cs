using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Deletes a price list with its items. A saved order is not touched: it keeps the price it was saved with (plan D10).</summary>
public class DeletePriceListHandler(IRepository<PriceList> lists, IDocumentVersion version, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<DeletePriceListCommand, Unit>
{
    public async Task<Unit> HandleAsync(DeletePriceListCommand cmd, CancellationToken ct)
    {
        var list = await lists.FirstOrDefaultAsync(new PriceListForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PriceListNotFound);
        version.EnsureCurrent(list.PublicId, list.Name, list, cmd.IfMatch);

        audit.Record(AuditActions.PriceListDeleted, AuditEntities.PriceList, list.PublicId.ToString(), ListAudit.Summary(list));
        lists.Remove(list); // its tracked items go with it (the persistence layer turns both deletes into soft deletes)
        await uow.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
