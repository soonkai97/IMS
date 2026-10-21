using Ims.Domain;
using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Purchasing;

public class PurchaseOrderReader(IReadRepository<PurchaseOrder> orders, IPurchaseOrderAccess access, IDocumentVersion version) : IPurchaseOrderReader, IAppService
{
    public async Task<PurchaseOrderDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        // The caller's warehouses are part of the query: an order elsewhere is simply not found (404, like an unknown id).
        var dto = await orders.FirstOrDefaultAsync(new PurchaseOrderDtoSpec(publicId, access.WarehouseScope()), ct) ?? throw new NotFoundException(Messages.PurchaseOrderNotFound);
        return dto with { ConcurrencyToken = version.Token(dto.Id, dto.RowVersion) };
    }
}
