using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class SetPriceListCustomerHandler(IRepository<PriceList> lists, IReadRepository<Partner> partners, IPriceListUniqueness uniqueness, IPriceListReader reader, IDocumentVersion version,
    IUnitOfWork uow) : ICommandHandler<SetPriceListCustomerCommand, PriceListDto>
{
    public async Task<PriceListDto> HandleAsync(SetPriceListCustomerCommand cmd, CancellationToken ct)
    {
        await uow.ExecuteAsync(async () =>
        {
            var list = await lists.FirstOrDefaultAsync(new PriceListHeaderForUpdateSpec(cmd.Id), ct) ?? throw new NotFoundException(Messages.PriceListNotFound);
            version.EnsureCurrent(list.PublicId, list.Name, list, cmd.IfMatch);
            var customer = cmd.Data.CustomerId is { } id ? await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(id), ct) ?? throw new ValidationException(Messages.PartnerMissing) : null;

            list.SetCustomer(customer);
            await uniqueness.EnsureAsync(list, ct);

            await uow.SaveChangesAsync(ct);
            return Unit.Value;
        }, ct);
        return await reader.GetAsync(cmd.Id, ct);
    }
}
