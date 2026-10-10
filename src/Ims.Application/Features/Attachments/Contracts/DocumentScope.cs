using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public record DocumentScope(int WarehouseId, DocumentStatus Status);
