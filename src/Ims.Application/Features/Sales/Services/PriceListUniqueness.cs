using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class PriceListUniqueness(IReadRepository<PriceList> lists) : IPriceListUniqueness, IAppService
{
    public async Task EnsureAsync(PriceList list, CancellationToken ct)
    {
        if (await lists.AnyAsync(new PriceListNameTakenSpec(list.Name, list.Id), ct)) throw new ConflictException(Messages.PriceListNameTaken(list.Name));
        if (list.IsDefault && await lists.FirstOrDefaultAsync(new DefaultPriceListSpec(list.Id), ct) is { } existing)
            throw new ConflictException(Messages.PriceListDefaultExists(existing.Name));
        if (list.CustomerId is { } customerId && await lists.FirstOrDefaultAsync(new CustomerPriceListSpec(customerId, list.Id), ct) is { } theirs)
            throw new ConflictException(Messages.PriceListCustomerHasList(list.Customer?.Name ?? "That customer", theirs.Name));
    }
}
