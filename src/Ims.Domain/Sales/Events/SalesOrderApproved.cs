namespace Ims.Domain;

/// <summary>A Submitted order was approved. Carries what the audit row of an approval prints.</summary>
public sealed record SalesOrderApproved(string No, SalesOrderStatus StatusBefore, SalesOrderStatus StatusAfter, bool IsLarge) : IDomainEvent;
