namespace Ims.Application.Features.Sales;

public record DeletePriceListCommand(Guid Id, string? IfMatch = null) : ICommand<Unit>;
