namespace Ims.Application.Features.Sales;

/// <summary>Reads one sales order as the caller may see it (warehouse scope in the query).</summary>
public interface ISalesOrderReader
{
    /// <exception cref="NotFoundException">No such order, or its warehouse is outside the caller's scope.</exception>
    Task<SalesOrderDto> GetAsync(Guid publicId, CancellationToken ct);
}
