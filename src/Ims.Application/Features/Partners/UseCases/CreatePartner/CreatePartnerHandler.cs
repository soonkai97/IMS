using Ims.Domain;
namespace Ims.Application.Features.Partners;

public class CreatePartnerHandler(IRepository<Partner> partners, IUnitOfWork uow) : ICommandHandler<CreatePartnerCommand, PartnerDto>
{
    public async Task<PartnerDto> HandleAsync(CreatePartnerCommand cmd, CancellationToken ct)
    {
        var p = Partner.Create(cmd.Data.Type, cmd.Data.Name, cmd.Data.Contact, cmd.Data.IsActive);
        partners.Add(p);
        await uow.SaveChangesAsync(ct);
        return new PartnerDto(p.Id, p.Type, p.Name, p.Contact, p.IsActive);
    }
}
