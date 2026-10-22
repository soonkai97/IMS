using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

public class UpdatePartnerHandler(IRepository<Partner> partners, IModuleSwitches modules, IUnitOfWork uow) : ICommandHandler<UpdatePartnerCommand, PartnerDto>
{
    public async Task<PartnerDto> HandleAsync(UpdatePartnerCommand cmd, CancellationToken ct)
    {
        var applyTaxIds = await modules.AcceptAsync(TaxFieldNames.RemovedModule, [(TaxFieldNames.Tin, cmd.Data.Tin.Value), (TaxFieldNames.SstRegNo, cmd.Data.SstRegNo.Value)], ct);
        var p = await partners.FirstOrDefaultAsync(new ByPublicIdSpec<Partner>(cmd.Id), ct) ?? throw new NotFoundException(Messages.PartnerNotFound);
        var name = cmd.Data.Name.Trim();
        if (await partners.AnyAsync(new PartnerNameTakenSpec(cmd.Data.Type, name, p.Id), ct)) throw new ConflictException(Messages.PartnerExists(cmd.Data.Type, name));
        p.Update(cmd.Data.Type, name, cmd.Data.Contact, cmd.Data.IsActive);
        if (applyTaxIds) p.SetTaxIdentifiers(cmd.Data.Tin.Or(p.Tin), cmd.Data.SstRegNo.Or(p.SstRegNo));
        await uow.SaveChangesAsync(ct);
        return await modules.ShapeAsync(new PartnerDto(p.PublicId, p.Type, p.Name, p.Contact, p.IsActive, p.Tin, p.SstRegNo), ct);
    }
}
