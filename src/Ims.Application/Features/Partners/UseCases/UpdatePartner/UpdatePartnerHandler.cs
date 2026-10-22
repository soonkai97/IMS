using Ims.Domain;
namespace Ims.Application.Features.Partners;

public class UpdatePartnerHandler(IRepository<Partner> partners, IUnitOfWork uow) : ICommandHandler<UpdatePartnerCommand, PartnerDto>
{
    public async Task<PartnerDto> HandleAsync(UpdatePartnerCommand cmd, CancellationToken ct)
    {
        var p = await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(cmd.Id), ct) ?? throw new NotFoundException(Messages.PartnerNotFound);
        var name = cmd.Data.Name.Trim();
        if (await partners.AnyAsync(new PartnerNameTakenSpec(cmd.Data.Type, name, p.Id), ct)) throw new ConflictException(Messages.PartnerExists(cmd.Data.Type, name));
        p.Update(cmd.Data.Type, name, cmd.Data.Contact, cmd.Data.IsActive);
        await uow.SaveChangesAsync(ct);
        return new PartnerDto(p.PublicId, p.Type, p.Name, p.Contact, p.IsActive);
    }
}
