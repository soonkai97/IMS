namespace Ims.Domain;

/// <summary>An Approved order was closed: as delivered (the posted delivery that took out the last quantity is named, no reason) or short (a reason, no delivery). Carries what the audit rows of both closes print.</summary>
public sealed record SalesOrderClosed(string No, SalesOrderStatus StatusBefore, SalesOrderStatus StatusAfter, SalesOrderCloseKind Kind, string? Reason, string? Delivery) : IDomainEvent;
