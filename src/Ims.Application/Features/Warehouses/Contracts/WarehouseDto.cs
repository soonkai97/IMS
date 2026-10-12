namespace Ims.Application.Features.Warehouses;

public record WarehouseDto(Guid Id, string Code, string Name, string? Contact, string? Notes, bool IsActive);
