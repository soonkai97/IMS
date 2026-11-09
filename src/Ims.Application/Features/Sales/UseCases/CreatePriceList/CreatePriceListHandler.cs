using Ims.Domain;
namespace Ims.Application.Features.Sales;

public class CreatePriceListHandler(IRepository<PriceList> lists, IReadRepository<Partner> partners, IPriceListItemBuilder builder, IPriceListUniqueness uniqueness, IPriceListReader reader,
    IUnitOfWork uow, IAuditTrail audit) : ICommandHandler<CreatePriceListCommand, PriceListDto>
{
    public async Task<PriceListDto> HandleAsync(CreatePriceListCommand cmd, CancellationToken ct)
    {
        // The whole body re-runs if two requests race for the same default, customer or name: the second then meets the check and gets its sentence, not a database error.
        var publicId = await uow.ExecuteAsync(async () =>
        {
            var r = cmd.Data;
            var customer = r.CustomerId is { } id ? await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(id), ct) ?? throw new ValidationException(Messages.PartnerMissing) : null;
            var list = PriceList.Create(r.Name, r.IsDefault, customer);
            var built = await builder.BuildAsync(r.Items, null, ct);
            list.ReplaceItems(built.Items);
            await uniqueness.EnsureAsync(list, ct);

            lists.Add(list);
            audit.Record(AuditActions.PriceListCreated, AuditEntities.PriceList, list.PublicId.ToString(), after: ListAudit.Summary(list, [], built));
            await uow.SaveChangesAsync(ct);
            return list.PublicId;
        }, ct);
        return await reader.GetAsync(publicId, ct);
    }
}
