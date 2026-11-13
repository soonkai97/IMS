using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

public sealed class PriceListReader(IReadRepository<PriceList> lists, IDocumentVersion version) : IPriceListReader, IAppService
{
    public async Task<PriceListDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        var list = await lists.FirstOrDefaultAsync(new PriceListWithItemsSpec(publicId), ct) ?? throw new NotFoundException(Messages.PriceListNotFound);
        var items = list.Items.OrderBy(i => i.Product.Name, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Product.Sku, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Uom.Code, StringComparer.OrdinalIgnoreCase)
            .Select(i => new PriceListItemDto(i.Product.PublicId, i.Product.Sku, i.Product.Name, i.Uom.PublicId, i.Uom.Code, i.ListPrice)).ToList();
        return new PriceListDto(list.PublicId, list.Name, list.IsDefault, list.Customer?.PublicId, list.Customer?.Name, items, list.RowVersion)
        {
            ConcurrencyToken = version.Token(list.PublicId, list.RowVersion)
        };
    }
}
