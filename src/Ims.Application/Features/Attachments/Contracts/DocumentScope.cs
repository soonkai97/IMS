using Ims.Domain;
namespace Ims.Application.Features.Attachments;

public sealed record DocumentScope(int WarehouseId, DocumentStatus Status);
