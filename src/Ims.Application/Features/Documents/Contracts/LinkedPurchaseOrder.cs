namespace Ims.Application.Features.Documents;

/// <summary>The purchase order a receipt was created from: its id and number. Omitted while the Purchasing module is off.</summary>
public sealed record LinkedPurchaseOrder(Guid Id, string No);
