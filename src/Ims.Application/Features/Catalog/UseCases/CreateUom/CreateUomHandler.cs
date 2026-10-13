using Ims.Domain;
namespace Ims.Application.Features.Catalog;

public class CreateUomHandler(IRepository<Uom> uoms, IUnitOfWork uow) : ICommandHandler<CreateUomCommand, UomDto>
{
    public async Task<UomDto> HandleAsync(CreateUomCommand cmd, CancellationToken ct)
    {
        var code = cmd.Data.Code.Trim().ToUpperInvariant();
        if (await uoms.AnyAsync(new UomCodeTakenSpec(code, 0), ct)) throw new ConflictException(Messages.UomExists);
        var u = Uom.Create(code, cmd.Data.Name);
        uoms.Add(u);
        await uow.SaveChangesAsync(ct);
        return new UomDto(u.PublicId, u.Code, u.Name);
    }
}
