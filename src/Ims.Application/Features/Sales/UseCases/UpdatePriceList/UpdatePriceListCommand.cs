namespace Ims.Application.Features.Sales;

public record UpdatePriceListCommand(Guid Id, PriceListSaveRequest Data, string? IfMatch = null) : ICommand<PriceListDto>;
