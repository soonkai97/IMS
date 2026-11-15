using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class UpdatePriceListHandler(IRepository<PriceList> lists, IReadRepository<Partner> partners, IPriceListItemBuilder builder, IPriceListUniqueness uniqueness, IPriceListReader reader,
    IDocumentVersion version, IUnitOfWork uow) : ICommandHandler<UpdatePriceListCommand, PriceListDto>
{
    public async Task<PriceListDto> HandleAsync(UpdatePriceListCommand cmd, CancellationToken ct)
    {
        await uow.ExecuteAsync(async () =>
        {
            var r = cmd.Data;
            var list = await lists.FirstOrDefaultAsync(new PriceListForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PriceListNotFound);
            version.EnsureCurrent(list.PublicId, list.Name, list, cmd.IfMatch);
            var customer = r.CustomerId is { } id ? await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(id), ct) ?? throw new ValidationException(Messages.PartnerMissing) : null;

            list.Update(r.Name, r.IsDefault, customer);
            list.ReplaceItems(await builder.BuildAsync(r.Items, list.Items.Select(i => (i.ProductId, i.UomId)).ToHashSet(), ct));
            await uniqueness.EnsureAsync(list, ct);

            await uow.SaveChangesAsync(ct);
            return Unit.Value;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
