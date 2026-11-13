namespace Ims.Application.Features.Documents;

/// <summary>The sales order a delivery was created from: its id and number. Omitted while the Sales module is off, and for a caller who may not read sales orders.</summary>
public sealed record LinkedSalesOrder(Guid Id, string No);
