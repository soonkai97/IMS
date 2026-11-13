namespace Ims.Application.Features.Sales;

public sealed class GetSalesOrderHandler(ISalesOrderReader reader) : IQueryHandler<GetSalesOrderQuery, SalesOrderDto>
{
    public Task<SalesOrderDto> HandleAsync(GetSalesOrderQuery q, CancellationToken ct) => reader.GetAsync(q.Id, ct);
}
