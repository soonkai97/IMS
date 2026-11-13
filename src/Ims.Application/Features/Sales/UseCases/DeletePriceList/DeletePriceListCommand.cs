namespace Ims.Application.Features.Sales;

public sealed record DeletePriceListCommand(Guid Id, string? IfMatch = null) : ICommand<Unit>;
