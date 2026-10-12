using Ims.Domain;
namespace Ims.Application.Features.Partners;

public class CreatePartnerHandler(IRepository<Partner> partners, IUnitOfWork uow) : ICommandHandler<CreatePartnerCommand, PartnerDto>
{
    public async Task<PartnerDto> HandleAsync(CreatePartnerCommand cmd, CancellationToken ct)
    {
        var name = cmd.Data.Name.Trim();
        if (await partners.AnyAsync(new PartnerNameTakenSpec(cmd.Data.Type, name, 0), ct)) throw new ConflictException(Messages.PartnerExists(cmd.Data.Type, name));
        var p = Partner.Create(cmd.Data.Type, name, cmd.Data.Contact, cmd.Data.IsActive);
        partners.Add(p);
        await uow.SaveChangesAsync(ct);
        return new PartnerDto(p.PublicId, p.Type, p.Name, p.Contact, p.IsActive);
    }
}
