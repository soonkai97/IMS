namespace Ims.Application.Features.Sales;

/// <summary>A sales order line as an earlier save left it: what a later save is compared with to see whether the line is new or repriced.</summary>
public sealed record SavedLine(int ProductId, int UomId, decimal Price);
