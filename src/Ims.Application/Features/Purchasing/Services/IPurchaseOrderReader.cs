namespace Ims.Application.Features.Purchasing;

/// <summary>Reads one purchase order as the caller may see it (warehouse scope in the query).</summary>
public interface IPurchaseOrderReader
{
    /// <exception cref="NotFoundException">No such order, or its warehouse is outside the caller's scope.</exception>
    Task<PurchaseOrderDto> GetAsync(Guid publicId, CancellationToken ct);
}
