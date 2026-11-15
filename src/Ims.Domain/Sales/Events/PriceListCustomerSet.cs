namespace Ims.Domain;

/// <summary>A price list was given to a customer or taken off one (null), through <c>SetCustomer</c>. Raised on every call that is valid, even when the customer stays the same: the audit row is written each time.</summary>
public sealed record PriceListCustomerSet(Guid ListId, int? CustomerBefore, int? CustomerAfter) : IDomainEvent;
