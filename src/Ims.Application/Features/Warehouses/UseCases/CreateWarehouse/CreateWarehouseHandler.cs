using Ims.Domain;
namespace Ims.Application.Features.Warehouses;

public class CreateWarehouseHandler(IRepository<Warehouse> warehouses, IUnitOfWork uow) : ICommandHandler<CreateWarehouseCommand, WarehouseDto>
{
    public async Task<WarehouseDto> HandleAsync(CreateWarehouseCommand cmd, CancellationToken ct)
    {
        var code = cmd.Data.Code.Trim().ToUpperInvariant();
        if (await warehouses.AnyAsync(new WarehouseCodeTakenSpec(code, 0), ct)) throw new ConflictException(Messages.WarehouseCodeExists);
        var w = Warehouse.Create(code, cmd.Data.Name, cmd.Data.Contact, cmd.Data.Notes, cmd.Data.IsActive);
        warehouses.Add(w);
        await uow.SaveChangesAsync(ct);
        return new WarehouseDto(w.PublicId, w.Code, w.Name, w.Contact, w.Notes, w.IsActive);
    }
}
