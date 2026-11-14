namespace Ims.Domain;

/// <summary>A Draft was submitted: it waits as Submitted for the reviewer of its tier, or is Approved at once when no approval rule fired. Carries what the audit row of a submit prints.</summary>
public sealed record SalesOrderSubmitted(string No, SalesOrderStatus StatusBefore, SalesOrderStatus StatusAfter, bool RequiresApproval, bool IsLarge, Money SellingAmount) : IDomainEvent;
