namespace Ims.Domain;

/// <summary>A reviewer sent a Submitted order back to Draft with a reason (trimmed). Carries what the audit row of a rejection prints.</summary>
public sealed record SalesOrderRejected(string No, SalesOrderStatus StatusBefore, SalesOrderStatus StatusAfter, string Reason) : IDomainEvent;
