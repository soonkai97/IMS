using Ims.Domain;
using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Sales;

public class SalesOrderReader(IReadRepository<SalesOrder> orders, ISalesOrderAccess access, IDocumentVersion version) : ISalesOrderReader, IAppService
{
    public async Task<SalesOrderDto> GetAsync(Guid publicId, CancellationToken ct)
    {
        // The caller's warehouses are part of the query: an order elsewhere is simply not found (404, like an unknown id).
        var dto = await orders.FirstOrDefaultAsync(new SalesOrderDtoSpec(publicId, access.WarehouseScope()), ct) ?? throw new NotFoundException(Messages.SalesOrderNotFound);
        return dto with { ConcurrencyToken = version.Token(dto.Id, dto.RowVersion) };
    }
}
