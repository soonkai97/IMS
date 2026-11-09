namespace Ims.Application.Features.Sales;

/// <param name="CustomerId">The customer who gets the list, or null to take the list off its customer. An active partner of type Customer; a customer has at most one list (409 otherwise); the default list has none (400).</param>
public record PriceListCustomerRequest(Guid? CustomerId);
