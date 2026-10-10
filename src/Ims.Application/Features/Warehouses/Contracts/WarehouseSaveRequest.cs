namespace Ims.Application.Features.Warehouses;

public record WarehouseSaveRequest(string Code, string Name, string? Contact, string? Notes, bool IsActive);
