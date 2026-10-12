namespace Ims.Application.Features.Import;

public record SkuRef(int Id, Guid PublicId, string Sku, bool IsActive, int BaseUomId);
