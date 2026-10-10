using Ims.Domain;
namespace Ims.Application.Features.Documents;

public record DocumentListItem(int Id, DocumentType Type, string No, DocumentStatus Status, string WarehouseName,
    string? PartnerName, DateTime CreatedAt, DateTime? PostedAt, decimal TotalQty, decimal TotalAmount, string CreatedBy);
