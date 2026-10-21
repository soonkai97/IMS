using Ims.Application.Features.Documents;
namespace Ims.Application.Features.Purchasing;

/// <summary>Creates the Draft receipt of an Approved purchase order, prefilled from what remains of each line at the order's price. Answers with the receipt.</summary>
public record CreateReceiptFromPurchaseOrderCommand(Guid OrderId) : ICommand<DocumentDto>;
