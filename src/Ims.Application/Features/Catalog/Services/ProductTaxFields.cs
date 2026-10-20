using Ims.Application.Features.Modules;
using Ims.Domain;
namespace Ims.Application.Features.Catalog;

/// <summary>Leaves the tax-ready fields out of a product response while the accounting module is off (the values stay stored and come back when it is switched on).</summary>
public static class ProductTaxFields
{
    public static async Task<ProductDto> ShapeAsync(this IModuleSwitches modules, ProductDto product, CancellationToken ct) =>
        await modules.ShowsAsync(ModuleCodes.Accounting, product.HsCode is not null || product.SstCode is not null, ct) ? product : Without(product);

    public static async Task<IReadOnlyList<ProductDto>> ShapeAsync(this IModuleSwitches modules, IReadOnlyList<ProductDto> products, CancellationToken ct) =>
        await modules.ShowsAsync(ModuleCodes.Accounting, products.Any(p => p.HsCode is not null || p.SstCode is not null), ct) ? products : products.Select(Without).ToList();

    private static ProductDto Without(ProductDto p) => p with { HsCode = null, SstCode = null };
}
