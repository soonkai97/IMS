namespace Ims.Application.Features.Sales;

public sealed record UpdatePriceListCommand(Guid Id, PriceListSaveRequest Data, string? IfMatch = null) : ICommand<PriceListDto>;
