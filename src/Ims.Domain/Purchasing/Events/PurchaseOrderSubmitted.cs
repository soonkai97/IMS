namespace Ims.Domain;

/// <summary>A Draft was submitted: it waits as Submitted for the reviewer of its tier, or is Approved at once when no approval rule fired. Carries what the audit row of a submit prints.</summary>
public sealed record PurchaseOrderSubmitted(string No, PurchaseOrderStatus StatusBefore, PurchaseOrderStatus StatusAfter, bool RequiresApproval, bool IsLarge, Money Amount) : IDomainEvent;
