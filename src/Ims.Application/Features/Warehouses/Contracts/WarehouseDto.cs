namespace Ims.Application.Features.Warehouses;

public record WarehouseDto(int Id, string Code, string Name, string? Contact, string? Notes, bool IsActive);
