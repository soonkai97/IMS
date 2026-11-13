using Ims.Domain;
namespace Ims.Application.Features.Documents;

public sealed record DocumentSaveRequest(Guid WarehouseId, Guid? PartnerId, IssuePurpose? Purpose, string? Remark, IReadOnlyList<DocumentLineInput> Lines);
