namespace Ims.Application.Features.Sales;

public sealed record CreatePriceListCommand(PriceListSaveRequest Data) : ICommand<PriceListDto>;
