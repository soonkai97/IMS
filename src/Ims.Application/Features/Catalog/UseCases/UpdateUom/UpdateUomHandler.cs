using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class UpdateUomHandler(IRepository<Uom> uoms, IUnitOfWork uow) : ICommandHandler<UpdateUomCommand, UomDto>
{
    public async Task<UomDto> HandleAsync(UpdateUomCommand cmd, CancellationToken ct)
    {
        var code = cmd.Data.Code.Trim().ToUpperInvariant();
        var u = await uoms.FirstOrDefaultAsync(new ByPublicIdSpec<Uom>(cmd.Id), ct) ?? throw new NotFoundException(Messages.UomNotFound);
        if (await uoms.AnyAsync(new UomCodeTakenSpec(code, u.Id), ct)) throw new ConflictException(Messages.UomExists);
        u.Update(code, cmd.Data.Name);
        await uow.SaveChangesAsync(ct);
        return new UomDto(u.PublicId, u.Code, u.Name);
    }
}
