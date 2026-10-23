using Ims.Domain;
namespace Ims.Application.Features.Sales;

public interface ISalesOrderLineBuilder
{
    /// <summary>Builds the lines of a draft. Every line that cannot be built is reported, not only the first.</summary>
    /// <exception cref="ValidationException">One or more lines name a product that does not exist, or break a rule of the line (inactive product, unit that does not convert, quantity or amount too large). Keys are <c>lines[i]</c>.</exception>
    Task<List<SalesOrderLine>> BuildAsync(IReadOnlyList<SalesOrderLineInput> inputs, CancellationToken ct);
}
