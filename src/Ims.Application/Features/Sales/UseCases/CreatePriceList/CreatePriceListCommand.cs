namespace Ims.Application.Features.Sales;

public record CreatePriceListCommand(PriceListSaveRequest Data) : ICommand<PriceListDto>;
