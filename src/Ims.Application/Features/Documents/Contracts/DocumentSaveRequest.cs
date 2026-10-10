using Ims.Domain;
namespace Ims.Application.Features.Documents;

public record DocumentSaveRequest(int WarehouseId, int? PartnerId, IssuePurpose? Purpose, string? Remark, IReadOnlyList<DocumentLineInput> Lines);
