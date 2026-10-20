using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Partners;

/// <summary>Leaves the tax-ready fields out of a partner response while the accounting module is off (the values stay stored and come back when it is switched on).</summary>
public static class PartnerTaxFields
{
    public static async Task<PartnerDto> ShapeAsync(this IModuleSwitches modules, PartnerDto partner, CancellationToken ct) =>
        await modules.ShowsAsync(ModuleCodes.Accounting, partner.Tin is not null || partner.SstRegNo is not null, ct) ? partner : Without(partner);

    public static async Task<IReadOnlyList<PartnerDto>> ShapeAsync(this IModuleSwitches modules, IReadOnlyList<PartnerDto> partners, CancellationToken ct) =>
        await modules.ShowsAsync(ModuleCodes.Accounting, partners.Any(p => p.Tin is not null || p.SstRegNo is not null), ct) ? partners : partners.Select(Without).ToList();

    private static PartnerDto Without(PartnerDto p) => p with { Tin = null, SstRegNo = null };
}
