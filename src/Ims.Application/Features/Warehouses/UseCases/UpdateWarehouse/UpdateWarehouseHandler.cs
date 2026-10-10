using Ims.Domain;
namespace Ims.Application.Features.Warehouses;

public class UpdateWarehouseHandler(IRepository<Warehouse> warehouses, ICurrentUser current, IUnitOfWork uow) : ICommandHandler<UpdateWarehouseCommand, WarehouseDto>
{
    public async Task<WarehouseDto> HandleAsync(UpdateWarehouseCommand cmd, CancellationToken ct)
    {
        // A supervisor assigned to one warehouse must not rename or deactivate another site's warehouse.
        if (!current.CanAccessWarehouse(cmd.Id)) throw new ForbiddenException(Messages.NoWarehouseAccess);
        var code = cmd.Data.Code.Trim().ToUpperInvariant();
        var w = await warehouses.GetByIdAsync(cmd.Id, ct) ?? throw new NotFoundException(Messages.WarehouseNotFound);
        if (await warehouses.AnyAsync(new WarehouseCodeTakenSpec(code, cmd.Id), ct)) throw new ConflictException(Messages.WarehouseCodeExists);
        w.Update(code, cmd.Data.Name, cmd.Data.Contact, cmd.Data.Notes, cmd.Data.IsActive);
        await uow.SaveChangesAsync(ct);
        return new WarehouseDto(w.Id, w.Code, w.Name, w.Contact, w.Notes, w.IsActive);
    }
}
