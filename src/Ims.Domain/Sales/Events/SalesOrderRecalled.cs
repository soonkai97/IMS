namespace Ims.Domain;

/// <summary>The submitter took a Submitted order back to Draft. Carries what the audit row of a recall prints (the before-state includes whether the order had waited for approval).</summary>
public sealed record SalesOrderRecalled(string No, SalesOrderStatus StatusBefore, bool RequiresApprovalBefore, SalesOrderStatus StatusAfter) : IDomainEvent;
