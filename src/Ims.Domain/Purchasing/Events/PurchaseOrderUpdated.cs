namespace Ims.Domain;

/// <summary>The header or the lines of a Draft were replaced. Carries the before and the after state that the audit row of an update prints.</summary>
public sealed record PurchaseOrderUpdated(string No, PurchaseOrderSummary Before, PurchaseOrderSummary After) : IDomainEvent;
