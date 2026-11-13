namespace Ims.Application.Features.Import;

public sealed record SkuRef(int Id, Guid PublicId, string Sku, bool IsActive, int BaseUomId);
