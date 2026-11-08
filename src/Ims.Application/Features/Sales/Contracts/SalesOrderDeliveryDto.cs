using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>A delivery created from a sales order, as the order lists it: its id (for the delivery screen), number and state. No money.</summary>
public record SalesOrderDeliveryDto(Guid Id, string No, DocumentStatus Status, DateTime CreatedAt, DateTime? PostedAt);
