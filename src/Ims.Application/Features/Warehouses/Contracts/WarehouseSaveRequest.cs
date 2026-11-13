namespace Ims.Application.Features.Warehouses;

public sealed record WarehouseSaveRequest(string Code, string Name, string? Contact, string? Notes, bool IsActive);
