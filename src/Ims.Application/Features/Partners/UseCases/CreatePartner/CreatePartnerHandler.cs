using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

public class CreatePartnerHandler(IRepository<Partner> partners, IModuleSwitches modules, IUnitOfWork uow) : ICommandHandler<CreatePartnerCommand, PartnerDto>
{
    public async Task<PartnerDto> HandleAsync(CreatePartnerCommand cmd, CancellationToken ct)
    {
        var applyTaxIds = await modules.AcceptAsync(ModuleCodes.Accounting, [(TaxFieldNames.Tin, cmd.Data.Tin.Value), (TaxFieldNames.SstRegNo, cmd.Data.SstRegNo.Value)], ct);
        var name = cmd.Data.Name.Trim();
        if (await partners.AnyAsync(new PartnerNameTakenSpec(cmd.Data.Type, name, 0), ct)) throw new ConflictException(Messages.PartnerExists(cmd.Data.Type, name));
        var p = Partner.Create(cmd.Data.Type, name, cmd.Data.Contact, cmd.Data.IsActive);
        if (applyTaxIds) p.SetTaxIdentifiers(cmd.Data.Tin.Or(p.Tin), cmd.Data.SstRegNo.Or(p.SstRegNo));
        partners.Add(p);
        await uow.SaveChangesAsync(ct);
        return await modules.ShapeAsync(new PartnerDto(p.PublicId, p.Type, p.Name, p.Contact, p.IsActive, p.Tin, p.SstRegNo), ct);
    }
}
