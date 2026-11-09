namespace Ims.Application.Features.Sales;

public record SetPriceListCustomerCommand(Guid Id, PriceListCustomerRequest Data, string? IfMatch = null) : ICommand<PriceListDto>;
