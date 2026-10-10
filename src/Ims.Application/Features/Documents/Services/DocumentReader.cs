using Ims.Domain;
namespace Ims.Application.Features.Documents;

public class DocumentReader(IReadRepository<StockDocument> documents, IDocumentAccess access, ISystemSettings settings)
    : IDocumentReader, IAppService
{
    public async Task<DocumentDto> GetAsync(int id, CancellationToken ct)
    {
        var dto = await documents.FirstOrDefaultAsync(new DocumentDtoSpec(id), ct) ?? throw new NotFoundException(Messages.DocumentNotFound);
        access.EnsureWarehouse(dto.WarehouseId);
        return dto with { AllowedActions = access.AllowedActions(dto, await settings.GetAsync(ct)) };
    }
}
