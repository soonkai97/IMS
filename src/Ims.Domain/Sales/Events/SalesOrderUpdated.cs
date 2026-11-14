namespace Ims.Domain;

/// <summary>The header or the lines of a Draft were replaced. Carries the before and the after state that the audit row of an update prints.</summary>
public sealed record SalesOrderUpdated(string No, SalesOrderSummary Before, SalesOrderSummary After) : IDomainEvent;
