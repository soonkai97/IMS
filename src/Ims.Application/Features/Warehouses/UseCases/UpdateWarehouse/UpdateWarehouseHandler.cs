using Ims.Domain;
namespace Ims.Application.Features.Warehouses;

public class UpdateWarehouseHandler(IRepository<Warehouse> warehouses, ICurrentUser current, IUnitOfWork uow) : ICommandHandler<UpdateWarehouseCommand, WarehouseDto>
{
    public async Task<WarehouseDto> HandleAsync(UpdateWarehouseCommand cmd, CancellationToken ct)
    {
        var code = cmd.Data.Code.Trim().ToUpperInvariant();
        // A supervisor assigned to one warehouse must not rename or deactivate another site's warehouse: it does not exist as far as they can tell (404, like an unknown id).
        var w = await warehouses.FirstOrDefaultAsync(new ByPublicIdSpec<Warehouse>(cmd.Id), ct);
        if (w is null || !current.CanAccessWarehouse(w.Id)) throw new NotFoundException(Messages.WarehouseNotFound);
        if (await warehouses.AnyAsync(new WarehouseCodeTakenSpec(code, w.Id), ct)) throw new ConflictException(Messages.WarehouseCodeExists);
        w.Update(code, cmd.Data.Name, cmd.Data.Contact, cmd.Data.Notes, cmd.Data.IsActive);
        await uow.SaveChangesAsync(ct);
        return new WarehouseDto(w.PublicId, w.Code, w.Name, w.Contact, w.Notes, w.IsActive);
    }
}
