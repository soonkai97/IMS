namespace Ims.Application.Features.Sales;

public sealed class GetPriceListHandler(IPriceListReader reader) : IQueryHandler<GetPriceListQuery, PriceListDto>
{
    public Task<PriceListDto> HandleAsync(GetPriceListQuery q, CancellationToken ct) => reader.GetAsync(q.Id, ct);
}
