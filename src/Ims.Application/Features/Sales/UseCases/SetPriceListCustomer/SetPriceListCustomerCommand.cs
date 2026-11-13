namespace Ims.Application.Features.Sales;

public sealed record SetPriceListCustomerCommand(Guid Id, PriceListCustomerRequest Data, string? IfMatch = null) : ICommand<PriceListDto>;
