namespace Ims.Application.Features.Sales;

public interface IPriceListReader
{
    /// <summary>One price list with its items and its concurrency token.</summary>
    /// <exception cref="NotFoundException">No such list.</exception>
    Task<PriceListDto> GetAsync(Guid publicId, CancellationToken ct);
}
