using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class UpdatePriceListHandler(IRepository<PriceList> lists, IReadRepository<Partner> partners, IPriceListItemBuilder builder, IPriceListUniqueness uniqueness, IPriceListReader reader,
    IDocumentVersion version, IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<UpdatePriceListCommand, PriceListDto>
{
    public async Task<PriceListDto> HandleAsync(UpdatePriceListCommand cmd, CancellationToken ct)
    {
        await uow.ExecuteAsync(async () =>
        {
            var r = cmd.Data;
            var list = await lists.FirstOrDefaultAsync(new PriceListForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PriceListNotFound);
            version.EnsureCurrent(list.PublicId, list.Name, list, cmd.IfMatch);
            var customer = r.CustomerId is { } id ? await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(id), ct) ?? throw new ValidationException(Messages.PartnerMissing) : null;

            var before = ListAudit.Summary(list);
            var beforeItems = ListAudit.Snapshot(list);
            list.Update(r.Name, r.IsDefault, customer);
            var built = await builder.BuildAsync(r.Items, list.Items.Select(i => (i.ProductId, i.UomId)).ToHashSet(), ct);
            list.ReplaceItems(built.Items);
            await uniqueness.EnsureAsync(list, ct);

            audit.Record(AuditActions.PriceListUpdated, AuditEntities.PriceList, list.PublicId.ToString(), before, ListAudit.Summary(list, beforeItems, built));
            await uow.SaveChangesAsync(ct);
            return Unit.Value;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
