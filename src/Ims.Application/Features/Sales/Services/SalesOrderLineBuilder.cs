using Ims.Application.Features.Documents;
using Ims.Domain;
namespace Ims.Application.Features.Sales;

/// <summary>Loads the products and units a draft's lines name (public ids, one query each for the whole order); the lines themselves (unit conversion, limits) are built by the domain.</summary>
public class SalesOrderLineBuilder(IReadRepository<Product> products, IIdMap<Uom> uomIds) : ISalesOrderLineBuilder, IAppService
{
    public async Task<List<SalesOrderLine>> BuildAsync(IReadOnlyList<SalesOrderLineInput> inputs, CancellationToken ct)
    {
        var ids = inputs.Select(i => i.ProductId).Distinct().ToList();
        var byId = (await products.ListAsync(new ProductsForLinesSpec(ids), ct)).ToDictionary(p => p.PublicId);

        // The units a client names are public ids; a unit that does not exist becomes the id of no row, which the domain refuses like any unit the product cannot convert.
        var units = await uomIds.ToInternalAsync(inputs.Where(i => i.UomId is not null).Select(i => i.UomId!.Value), ct);

        var lines = new List<SalesOrderLine>(inputs.Count);
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            try
            {
                if (!byId.TryGetValue(input.ProductId, out var product)) throw new DomainException(Messages.ProductMissing(input.ProductId));
                int? uomId = input.UomId is { } unit ? units.GetValueOrDefault(unit, IdMapExtensions.NoRow) : null;
                lines.Add(SalesOrderLine.Create(product, input.Qty, uomId, input.UnitPrice, input.Remark));
            }
            catch (DomainException e)
            {
                errors[ErrorKeys.Normalize($"Data.Lines[{i}]")] = [e.Message]; // the name the client sent: lines[2]
            }
        }
        if (errors.Count > 0) throw new ValidationException(errors.First().Value[0], errors);
        return lines;
    }
}
