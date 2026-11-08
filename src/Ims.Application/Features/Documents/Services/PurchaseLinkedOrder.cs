using Ims.Domain;
namespace Ims.Application.Features.Documents;

/// <summary>The purchase order of a receipt created from one.</summary>
public sealed record PurchaseLinkedOrder(PurchaseOrder Order) : LinkedOrder;
