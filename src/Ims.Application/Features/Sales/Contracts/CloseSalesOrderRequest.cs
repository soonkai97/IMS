namespace Ims.Application.Features.Sales;

/// <param name="Reason">Why what is still to go out will not go: required, at least 5 characters once trimmed.</param>
public record CloseSalesOrderRequest(string? Reason);
