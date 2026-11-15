namespace Ims.Domain;

/// <summary>A purchase order was created: raised when its lines are in, because the audit row of a create names the number and counts the lines. Carries what that audit row prints as its after-state.</summary>
public sealed record PurchaseOrderCreated(string No, PurchaseOrderSummary Summary) : IDomainEvent;
