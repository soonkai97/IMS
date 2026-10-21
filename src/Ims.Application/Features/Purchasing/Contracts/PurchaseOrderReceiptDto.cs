using Ims.Domain;
namespace Ims.Application.Features.Purchasing;

/// <summary>A receipt created from a purchase order, as the order lists it: its id (for the receipt screen), number and state. No money.</summary>
public record PurchaseOrderReceiptDto(Guid Id, string No, DocumentStatus Status, DateTime CreatedAt, DateTime? PostedAt);
